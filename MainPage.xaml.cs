using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Net;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using CommunityToolkit.Maui.Media;
using System.ComponentModel.Design;
using System.IO.Enumeration;


//schau im EDITOR
namespace BlueShare
{
    public partial class MainPage : ContentPage
    {        
        private UdpClient _udpClient = new UdpClient(5000);
        Dictionary<string, string> _devices = new Dictionary<string, string>();

        private bool _connect = false;
        private bool _searching = false;
        private bool gotPinged;

        Stream stream = null;
        TcpClient Sender = null;
        StreamWriter writer = null;
        StreamReader reader = null;
        
        TcpClient client = new TcpClient();
        //Client for sending data
        public MainPage()
        {
            InitializeComponent();
        }

        private async void SearchDevices(object sender, EventArgs e)
        {
            if (_searching || _connect)
            {
                ChangeLabelText(Main_L, "Stopping the process...");
                _searching = false;
                _connect = false;
                Devices.Children.Clear();
                _devices.Clear();
                ChangeVisibility(Button_1, true);
                ChangeVisibility(ScrollView, false);
                ChangeVisibility(Button_3, false);
                if (client.Connected) 
                { 
                    client.Close();
                    client = new TcpClient();
                }
                
            }
            else
            {
                _searching = true;
                ChangeLabelText(Second_L, "Searching started");
                ChangeLabelText(Main_L, "Click again to stop searching");
                ChangeVisibility(Button_1, false);
                ChangeVisibility(ScrollView, true);
            }
            

            while (_searching)
            {
                using (var cts = new CancellationTokenSource(5000))
                {
                    try
                    {
                        byte[] search = System.Text.Encoding.UTF8.GetBytes("Searching BlueShare");
                        await _udpClient.SendAsync(search, search.Length, "255.255.255.255", 5000);

                        var message = await _udpClient.ReceiveAsync(cts.Token);
                        byte[] data = message.Buffer;
                        string result = System.Text.Encoding.UTF8.GetString(data);

                        if (result.StartsWith("RESPONSE|"))
                        {
                            string[] parts = result.Split('|');

                            if (_devices.ContainsKey(parts[1])) { continue; }

                            _devices.Add(parts[1], parts[2]);

                            //Button
                            Button Device = new Button
                            {
                                Text = $"Name: {parts[1]}\nIP: {parts[2]}",
                                HorizontalOptions = LayoutOptions.Center,
                                WidthRequest = 250,
                                CommandParameter = parts[1]
                            };
                            Device.Clicked += ConnectToDevice;
                            Device.BackgroundColor = Color.FromArgb("#1769FF");
                            Device.TextColor = Color.FromArgb("#FFFFFF");
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                Devices.Children.Add(Device);
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("Timeout beim Warten auf UDP-Antwort. Suche läuft weiter...");
                        _devices.Clear();
                        await MainThread.InvokeOnMainThreadAsync(() => 
                        {
                            Devices.Children.Clear();
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Fehler bei der Suche: {ex.Message}");
                    }
                }

                await Task.Delay(100);
            }

            _searching = false;
            ChangeLabelText(Main_L, "Choose what do do");
            ChangeLabelText(Second_L, "Searching stopped!");
        }

        private async void ConnectToDevice(Object? sender, EventArgs e)
        {
            _searching = false;
            string deviceIP;
            if (sender is Button clickedButton && clickedButton.CommandParameter is string deviceName)
            {
                deviceIP = _devices[deviceName];
            }
            else { return; }

            //Client settings for more Speed 
            client.NoDelay = true;

            client.ReceiveBufferSize = 256 * 1024;
            client.SendBufferSize = 256 * 1024;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);


            try
            {
                await client.ConnectAsync(deviceIP, 5001);

                stream = client.GetStream();
                writer = new StreamWriter(stream);
                reader = new StreamReader(stream);

                var message = await reader.ReadLineAsync();

                if (message == "OK")
                {
                    ChangeLabelText(Main_L, "Connection accepted");
                    _connect = true;
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        Devices.Clear();
                        Button_3.IsVisible = true;
                    });
                }
                else if (message == "NO")
                {
                    ChangeLabelText(Main_L, "Connection denied");
                    _connect = false;
                    client.Close();
                    client = new TcpClient();
                }
            }
            catch
            {
                ChangeLabelText(Second_L, "Problems while Connecting. try again");
            }
        }

        private async void SendData(Object? sender, EventArgs e)
        {
            
            ChangeLabelText(Second_L, "Pls Choose your data");

            string action = await DisplayActionSheetAsync("Data from:", "Cancel", null, "Camera", "Galerie (Photo/Video)", "Document");
            FileResult data;

            switch (action)
            {
                case "Camera":
                    data = await TakePhotoAsync();
                    break;
                case "Galerie (Photo/Video)":
                    data = await PickImage();
                    break;
                case "Document":
                    data = await PickDocument();
                    break;
                default:
                    data = null;
                    break;
            }

            if (data == null)
            {
                ChangeLabelText(Second_L, "Couldnt load your Data. Try again");
                return;
            }

            string dataPath = data.FullPath;

            ChangeLabelText(Second_L, "Data will now be sended...");
            try
            {
                Stream fileStream = await data.OpenReadAsync();

                long fileSize = fileStream.Length;
                byte[] dataSize = BitConverter.GetBytes(fileSize);
                await stream.WriteAsync(dataSize, 0, 8);

                string contentType = string.IsNullOrEmpty(data.ContentType) ? "application/octet-stream" : data.ContentType;
                byte[] dataTypeBytes = Encoding.UTF8.GetBytes(contentType);
                byte[] dataTypeLength = BitConverter.GetBytes(dataTypeBytes.Length);

                await stream.WriteAsync(dataTypeLength, 0, 4);
                await stream.WriteAsync(dataTypeBytes, 0, dataTypeBytes.Length);
                await stream.FlushAsync();

                byte[] buffer = new byte[256 * 1024];
                int bytesRead;
                long totalBytesSent = 0;

                var stopWatch = Stopwatch.StartNew();

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, bytesRead);

                    totalBytesSent += bytesRead;

                    if (stopWatch.ElapsedMilliseconds > 300 || totalBytesSent == fileSize)
                    {
                        int procentuale = (int)((totalBytesSent * 100) / fileSize);
                        ChangeLabelText(Second_L, $"{procentuale}%");
                        stopWatch.Restart();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            await stream.FlushAsync();
            ChangeLabelText(Second_L, "Data sucessfully sended");
        }

        private async void ReceiveData(Object? sender, EventArgs e)
        {
            TcpListener Receiver = new TcpListener(IPAddress.Any, 5001);

            if (_searching || _connect)
            {
                _searching = false;
                _connect = false;
                ChangeVisibility(Button_2, true);
                ChangeLabelText(Main_L, "Sucessfully Stopped!");
                ChangeLabelText(Second_L, "Choose what to do next");
                if (Sender != null && Sender.Connected)
                {
                    Sender.Close();
                    Sender = null;
                    Receiver = null;
                }
                return;
            }

            gotPinged = false;
            _searching = true;
            ChangeVisibility(Button_2, false);
            ChangeLabelText(Main_L, "Waiting for Connection...");

            _ = Task.Run(async () =>
            {
                await Searchloop();
            });

            while (_searching && !gotPinged)
            {
                await Task.Delay(200);
            }

            if (!_searching) return;

            bool answer = false;
            bool networkIsWorking = false;

            using (var cts = new CancellationTokenSource(10000))
            {
                
                try
                {
                    Receiver.Start();

                    Sender = await Receiver.AcceptTcpClientAsync(cts.Token);
                    
                    Sender.NoDelay = true;
                    Sender.ReceiveBufferSize = 256 * 1024;
                    Sender.SendBufferSize = 256 * 1024;
                    Sender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    

                    stream = Sender.GetStream();
                    reader = new StreamReader(stream);
                    writer = new StreamWriter(stream);
                    networkIsWorking = true;
                    gotPinged = false;
                    _connect = true;
                    
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ChangeLabelText(Second_L, ex.Message);
                }
                finally
                {
                    Receiver.Stop();
                }
            }

            if (!networkIsWorking || Sender == null || writer == null || reader == null) { return; }

            answer = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                return await DisplayAlertAsync("Connect", "You want to connect to " + Sender.Client.RemoteEndPoint.ToString(), "Yes", "No");
            });

            if (answer)
            {
                _searching = false;
                ChangeLabelText(Main_L, "Connection started");

                string message = "OK";
                await writer.WriteLineAsync(message);
                await writer.FlushAsync();

                while (_connect)
                {
                    try
                    {
                        byte[] dataSize = new byte[8];
                        await stream.ReadExactlyAsync(dataSize, 0, 8);

                        long fileSize = BitConverter.ToInt64(dataSize, 0);

                        byte[] typeLengthBytes = new byte[4];
                        await stream.ReadExactlyAsync(typeLengthBytes, 0, 4);
                        int typeLength = BitConverter.ToInt32(typeLengthBytes, 0);

                        byte[] typeBytes = new byte[typeLength];
                        await stream.ReadExactlyAsync(typeBytes, 0, typeLength);
                        string dataType = Encoding.UTF8.GetString(typeBytes);

                        var fileSaveResult = new CommunityToolkit.Maui.Storage.FileSaverResult(null, null);

                        if (fileSize <= 2048)
                        {
                            using (MemoryStream memoryStream = new MemoryStream())
                            {
                                byte[] buffer = new byte[265 * 1024];
                                long totalBytesReceived = 0;

                                while (totalBytesReceived < fileSize)
                                {
                                    int bytesToRead = (int)Math.Min(buffer.Length, fileSize - totalBytesReceived);
                                    int currentRead = await stream.ReadAsync(buffer, 0, bytesToRead);

                                    if (currentRead == 0)
                                    {
                                        break;
                                    }

                                    await memoryStream.WriteAsync(buffer, 0, currentRead);
                                    totalBytesReceived += currentRead;
                                }

                                memoryStream.Position = 0;

                                string dataName = $"received.{dataType.Split('/')[1]}";

                                fileSaveResult = await MainThread.InvokeOnMainThreadAsync(async () =>
                                {
                                    return await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(
                                        dataName,
                                        memoryStream,
                                        CancellationToken.None
                                    );
                                });
                            }
                        }
                        else
                        {
                            string cachedir = FileSystem.CacheDirectory;

                            string fileName = $"received.{dataType.Split('/')[1]}";
                            string fullPath = Path.Combine(cachedir, fileName);

                            try
                            {
                                byte[] buffer = new byte[256 * 1024];
                                long totalBytesReceived = 0;

                                await Task.Run(async () =>
                                {
                                    using (FileStream fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024))
                                    using(BufferedStream bufferedStream = new BufferedStream(fileStream, 256 * 1024))
                                    {
                                        while (totalBytesReceived < fileSize)
                                        {
                                            int bytesToRead = (int)Math.Min(buffer.Length, fileSize - totalBytesReceived);
                                            int currentRead = stream.Read(buffer, 0, bytesToRead);
                                            if (currentRead == 0) break;

                                            await bufferedStream.WriteAsync(buffer, 0, currentRead);
                                            totalBytesReceived += currentRead;
                                        }
                                    }
                                });

                                fileSaveResult = await MainThread.InvokeOnMainThreadAsync(async () =>
                                {
                                    using (var fileStream = File.OpenRead(fullPath))
                                    {
                                        return await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(
                                            fileName,
                                            fileStream,
                                            CancellationToken.None
                                        );
                                    }
                                });
                            }
                            finally
                            {
                                if (File.Exists(fullPath)) { File.Delete(fullPath); }
                            }


                        }

                        if (fileSaveResult.IsSuccessful)
                        {
                            ChangeLabelText(Second_L, "Transfer was succesfull");
                        }
                        else
                        {
                            ChangeLabelText(Second_L, "Something went wrong.");
                        }
                    }

                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                        ChangeLabelText(Second_L, "Exception while receiving data or saving data");
                    }
                }
            }
            else
            {
                ChangeLabelText(Main_L, "Connection denied");
                string message = "NO";
                await writer.WriteLineAsync(message);
                await writer.FlushAsync();
                _connect = false;
                Sender.Close();
            }
        }

        public static string GetLocalIPAddress()
        {
            string? fallbackIP = null;
            foreach (var netinterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipPros = netinterface.GetIPProperties();

                foreach (var addr in ipPros.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = addr.Address.ToString();

                        if (!IPAddress.IsLoopback(addr.Address) && !ipStr.StartsWith("169.254"))
                        {
                            return ipStr;
                        }
                        else
                        {
                            fallbackIP = ipStr;
                        }
                    }
                }
            }
            return fallbackIP ?? throw new Exception("No IPv4 addres found!");
        }

        private async Task<FileResult?> TakePhotoAsync()
        {
            if (MediaPicker.Default.IsCaptureSupported)
            {
                FileResult data = await MediaPicker.Default.CapturePhotoAsync();
                if (data != null)
                {
                    return data;
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlertAsync("Error", "Camera is not available", "Ok");
                    });
                }
            }
            return null;
        }

        private async Task Searchloop()
        {
            while (_searching)
            {
                using (var cts = new CancellationTokenSource(5000))
                {
                    try
                    {
                        var message = await _udpClient.ReceiveAsync(cts.Token);
                        byte[] data = message.Buffer;
                        string result = System.Text.Encoding.UTF8.GetString(data);

                        if (result == "Searching BlueShare")
                        {
                            string name = Microsoft.Maui.Devices.DeviceInfo.Current.Name.Trim();
                            string response = $"RESPONSE|{name}|{GetLocalIPAddress()}";
                            byte[] responseData = System.Text.Encoding.UTF8.GetBytes(response);
                            _udpClient.Send(responseData, responseData.Length, message.RemoteEndPoint);

                            ChangeLabelText(Second_L, "Got Pinged!");
                            gotPinged = true;
                        }
                    }
                    catch (OperationCanceledException) { }
                }
            }
        }

        private async Task<FileResult?> PickImage()
        {
            FileResult data = await MediaPicker.Default.PickPhotoAsync();
            if (data != null)
            {
                return data;
            }
            return null;
        }

        private async Task<FileResult?> PickDocument()
        {
            var data = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pls pick your document"
            });

            if (data != null)
            {
                return data;
            }
            return null;
        }

        async void ChangeLabelText(Label name, string text)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                name.Text = text;
            });
        }

        async void ChangeVisibility(VisualElement name, bool visible)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                name.IsVisible = visible;
            });
        }
    }
}