using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace ServerChatApplication
{
    using ChatApplication.Helper;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // ============================
        // === Fields & Props
        // ============================

        private IPEndPoint serverEndpoint;

        private Socket serverSocket;

        private CancellationTokenSource cts;

        private List<Socket> clientSockets = new List<Socket>();

        // ============================
        // === Constructor
        // ============================
        public MainWindow()
        {
            InitializeComponent();
            InitializeChatServer();
        }

        // ============================
        // === Methods
        // ============================

        private void InitializeChatServer()
        {
            // 1. Binding Socket to Endpoint
            serverEndpoint = new IPEndPoint(NetworkingHelper.SERVER_IPADDRESS, NetworkingHelper.SERVER_PORT);
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(serverEndpoint);

            // 2. Turn on Listening Mode
            // 2. Send <TURNOFF> mode to close the server connection
            serverSocket.Listen(NetworkingHelper.SERVER_BACKLOG);
            PrintMsgToTextBox($"Listening on {NetworkingHelper.SERVER_IPADDRESS}:{NetworkingHelper.SERVER_PORT}");
            PrintMsgToTextBox("Enter <SHUTDOWN> to shutdown chat server.");

            // 3. Create Cancellation Token
            cts = new CancellationTokenSource();

            // 4. Channel to process incoming connection from clients
            _ = AcceptClientConnectionAsync(cts.Token);
        }

        /// <summary>
        /// Iterating for accepting client connection
        /// </summary>
        /// <param name="serverSocket"></param>
        /// <param name="token"></param>
        private async Task AcceptClientConnectionAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var clientSocket = await serverSocket.AcceptAsync(token);
                PrintMsgToTextBox($"[{clientSocket.RemoteEndPoint}] connected.");

                // Add client to sockets for broadcasting
                clientSockets.Add(clientSocket);

                // Handle client connection asynchronously
                _ = HandleClientConnectionAsync(clientSocket, token);
            }
        }

        /// <summary>
        /// Handle Client Connection
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="token"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task HandleClientConnectionAsync(Socket clientSocket, CancellationToken token)
        {
            CancellationTokenSource timeoutCts = new CancellationTokenSource(1000000000);
            CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, token);

            // Sending Welcome Message to the connected client
            byte[] byteWelcomeText = Encoding.UTF8.GetBytes(NetworkingHelper.SERVER_WELCOME_TEXT);
            await clientSocket.SendAsync(byteWelcomeText);

            // Looping to recieve async message from client 
            while (!linkedCancellation.IsCancellationRequested)
            {
                try
                {
                    // Receiving message sending from client
                    byte[] receivedBuffer = new byte[4028];
                    var receivedBufferLen = await clientSocket.ReceiveAsync(receivedBuffer);
                    string receivedText = Encoding.UTF8.GetString(receivedBuffer, 0, receivedBufferLen);

                    // Check File
                    if (receivedText.StartsWith("FILE:"))
                    {
                        // Parse header
                        var parts = receivedText.Split(':');
                        string fileName = parts[1];
                        int fileSize = int.Parse(parts[2]);

                        byte[] fileData = new byte[fileSize];
                        int totalBytesReceived = 0;
                        while (totalBytesReceived < fileSize)
                        {
                            int bytesReceived = await clientSocket.ReceiveAsync(fileData.AsMemory(totalBytesReceived));
                            totalBytesReceived += bytesReceived;
                        }

                        PrintMsgToTextBox($"Received file: {fileName}", false, true, fileName, fileData);
                        // Boardcasting to all client within the network
                        _ = BroadcastingMsgToClients(receivedText, clientSocket, token, fileName, fileData);
                    }
                    else
                    {
                        PrintMsgToTextBox(receivedText, false);
                        _ = BroadcastingMsgToClients(receivedText, clientSocket, token);
                    }
                }
                catch (OperationCanceledException ex) when (linkedCancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Boarding message to all clients within the network
        /// </summary>
        /// <param name="receivedText"></param>
        /// <param name="excludeClient"></param>
        /// <param name="token"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task BroadcastingMsgToClients(string receivedText, Socket excludeClient, CancellationToken token, string? fileName = null, byte[]? fileData = null)
        {
            string formattedMsg = $"{excludeClient.RemoteEndPoint} - {receivedText}";

            foreach (var clientSocket in clientSockets)
            {
                // Sending all exception for the client that sends the receivedText
                if (clientSocket != excludeClient)
                {
                    if (fileData != null && fileName != null)
                    {
                        // Gửi header thông tin file
                        string fileHeader = $"FILE:{fileName}:{fileData.Length}";
                        byte[] headerBytes = Encoding.UTF8.GetBytes(fileHeader);
                        await clientSocket.SendAsync(headerBytes, SocketFlags.None);

                        // Gửi dữ liệu file theo từng chunk
                        int chunkSize = 4096; // Kích thước chunk cố định
                        int totalBytesSent = 0;
                        while (totalBytesSent < fileData.Length)
                        {
                            int bytesToSend = Math.Min(chunkSize, fileData.Length - totalBytesSent);
                            await clientSocket.SendAsync(fileData.AsMemory(totalBytesSent, bytesToSend), SocketFlags.None);
                            totalBytesSent += bytesToSend;
                        }
                    }
                    else
                    {
                        NetworkStream networkStream = new NetworkStream(clientSocket);
                        StreamWriter writerStream = new StreamWriter(networkStream);
                        await writerStream.WriteLineAsync(formattedMsg);
                        await writerStream.FlushAsync(token);
                        await networkStream.FlushAsync(token);
                    }

                }
            }
        }

        /// <summary>
        /// Print msg to the msg box 
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="isCurrentSocket"></param>
        private void PrintMsgToTextBox(string msg, bool isCurrentSocket = false, bool isFile = false, string fileName = null, byte[] fileData = null)
        {
            this.Dispatcher.Invoke(() =>
            {
                var msgContainer = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(5)
                };

                var lblMessage = new TextBlock
                {
                    Text = isCurrentSocket ? $"(SERVER) {msg}" : $"(Client) {msg}",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                msgContainer.Children.Add(lblMessage);

                if (isFile && !string.IsNullOrEmpty(fileName))
                {
                    var btnDownload = new Button
                    {
                        Content = "Download",
                        Background = Brushes.LightBlue,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    btnDownload.Click += (s, e) =>
                    {
                        string downloadsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                        string tempFilePath = System.IO.Path.Combine(downloadsPath, fileName);
                        File.WriteAllBytes(tempFilePath, fileData);
                    };

                    msgContainer.Children.Add(btnDownload);
                }

                chatPanel.Children.Add(msgContainer);
            });
        }

        /// <summary>
        /// Clicking button to send message
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var text = txtBoxInputChat.Text;

            try
            {
                await BroadcastingMsgToClients(text, serverSocket, cts.Token);

                // Print Msg into text box
                PrintMsgToTextBox(text, true);
            }

            catch (SocketException ex)
            {
                PrintMsgToTextBox($"Socket error: {ex.Message}", true);
            }
            catch (Exception ex)
            {
                PrintMsgToTextBox($"Socket error: {ex.Message}", true);
            }
        }
    }
}
