using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

using Newtonsoft.Json;

namespace RosenHCMC.VPN.Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly HttpClient client = new HttpClient();

        public ObservableCollection<WallpaperDTO> imageList = new ObservableCollection<WallpaperDTO>();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            imageListBox.ItemsSource = imageList;
        }

        private string JpgToB64(string fileName)
        {
            System.Drawing.Image img = System.Drawing.Image.FromFile(fileName);
            System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(img);
            using (MemoryStream png = new MemoryStream())
            {
                bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                byte[] byteImage = png.ToArray();
                return Convert.ToBase64String(byteImage);
            }
        }

        private string PngToB64(string fileName)
        {
            byte[] imageBytes = File.ReadAllBytes(fileName);
            return Convert.ToBase64String(imageBytes);
        }

        private void uploadButton_Click(object sender, RoutedEventArgs e)
        {
            string path = imagePath.Text;
            string fileName = Path.GetFileName(path);
            string extension = Path.GetExtension(fileName).Substring(1);
            if (!File.Exists(path))
            {
                Console.WriteLine("{0} does not exist", path);
            }

            string b64;
            switch (extension)
            {
                case "png":
                    b64 = PngToB64(path);
                    break;

                case "jpg":
                case "jpeg":
                    b64 = JpgToB64(path);
                    break;
                default:
                    b64 = null;
                    break;
            }

            var obj = new
            {
                WallpaperFileName = fileName,
                WallpaperContent = b64
            };
            try
            {
                StringContent content = new StringContent(JsonConvert.SerializeObject(obj));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                HttpResponseMessage httpResult = client.PostAsync("/api/wallpaper", content).Result;
                string result = httpResult.Content.ReadAsStringAsync().Result;
                logTextBoxAdd(result);
            }
            catch (AggregateException aggex)
            {
                foreach (var ex in aggex.InnerExceptions)
                {
                    logTextBoxAdd(ex.Message);
                }
            }
            catch (Exception ex)
            {
                logTextBoxAdd(ex.Message);
            }
        }

        private async Task<string[]> DiscoveryServer()
        {
            UdpClient Client = new UdpClient();
            byte[] RequestData = Encoding.ASCII.GetBytes("SomeRequestData");
            IPEndPoint ServerEp = new IPEndPoint(IPAddress.Any, 0);

            Client.EnableBroadcast = true;
            Client.Send(RequestData, RequestData.Length, new IPEndPoint(IPAddress.Broadcast, 9099));

            byte[] ServerResponseData = await Task.Run(() => Client.Receive(ref ServerEp));
            string ServerResponse = Encoding.ASCII.GetString(ServerResponseData);
            Console.WriteLine("Recived {0} from {1}", ServerResponse, ServerEp.Address.ToString());
            string[] ips = JsonConvert.DeserializeObject<string[]>(ServerResponse);

            Client.Close();

            return ips;
        }

        private async void discoveryButton_Click(object sender, RoutedEventArgs e)
        {
            int timeout = 5000;
            Task<string[]> task = DiscoveryServer();
            if (await Task.WhenAny(task, Task.Delay(timeout)) == task)
            {
                // task completed within timeout
                serverSelectListBox.ItemsSource = await task;
            }
            else
            {
                // timeout logic
                //logTextBoxAdd("Timeout when discovery server!");
                logTextBoxAdd("Timeout when discover server!");
            }
        }

        private void imagePath_DoubleClicked(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                logTextBoxAdd(openFileDialog.FileName);

                imagePath.Text = openFileDialog.FileName;
            }
        }

        private void logTextBoxAdd(string msg)
        {
            logTextBox.Text += msg + Environment.NewLine;
            logTextBox.ScrollToEnd();
        }

        private void serverSelectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (object item in e.AddedItems)
            {
                try
                {
                    //client.BaseAddress = new Uri("http://" + item.ToString() + ":9091");
                    ipTextbox.Text = item.ToString();
                }
                catch (Exception ex)
                {
                    logTextBoxAdd(ex.Message);
                }
            }
        }

        private void clearLogButton_Click(object sender, RoutedEventArgs e)
        {
            logTextBox.Text = string.Empty;
        }

        private void refreshButton_Click(object sender, RoutedEventArgs e)
        {
            HttpResponseMessage httpResult = client.GetAsync("/api/wallpaper/content").Result;
            string result = httpResult.Content.ReadAsStringAsync().Result;
            imageList.Clear();
            JsonConvert.DeserializeObject<List<WallpaperDTO>>(result).ForEach(imageList.Add);
            logTextBoxAdd($"fetched {imageList.Count} images from {client.BaseAddress}");
        }

        private void imageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (object item in e.AddedItems)
            {
                try
                {
                    WallpaperDTO wp = (WallpaperDTO)item;
                    SetImage(wp.WallpaperContent);
                }
                catch (Exception ex)
                {
                    logTextBoxAdd(ex.Message);
                }
            }
        }

        private void SetImage(string wallpaperContent)
        {
            byte[] binaryData = Convert.FromBase64String(wallpaperContent);

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(binaryData);
            bitmap.EndInit();
            previewImage.Source = bitmap;
        }

        private void setWallpaper_Click(object sender, RoutedEventArgs e)
        {
            if (imageListBox.SelectedItem == null)
            {
                logTextBoxAdd("Please select an image");
                return;
            }
            WallpaperDTO wp = (WallpaperDTO)imageListBox.SelectedItem;
            HttpResponseMessage httpResult = client.PutAsync($"/api/wallpaper/{wp.Id}", new StringContent(string.Empty)).Result;
            if (httpResult.IsSuccessStatusCode)
            {
                logTextBoxAdd("Wallpaper Request succeeded");
            }
            else
            {
                logTextBoxAdd("Wallpaper Request failed");
            }
        }

        private void setButton_Click(object sender, RoutedEventArgs e)
        {
            bool isIp = IPAddress.TryParse(ipTextbox.Text, out IPAddress ip);
            if (isIp)
            {
                client.BaseAddress = new Uri("http://" + ipTextbox.Text + ":9091");
                logTextBoxAdd($"set server to :{client.BaseAddress}");
            }
            else
            {
                logTextBoxAdd("The inputted text is not a valid ip");
            }
        }
    }
}
