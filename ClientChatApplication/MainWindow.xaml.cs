using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ChatApplication.Helper;
using Microsoft.Win32;
using System.IO;
using System.Reflection.PortableExecutable;
using static System.Net.Mime.MediaTypeNames;

namespace ClientChatApplication
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // ============================
        // === Fields & Props
        // ============================

        private IPEndPoint serverEndpoint = null;

        private Socket clientSocket = null;

        private static int BUFFER_SIZE = 1024;

        private CancellationTokenSource cts;

        // ============================
        // === Constructor
        // ============================

        public MainWindow()
        {
            InitializeComponent();
            _ = InitializeChatClient();
        }

        // ============================
        // === Methods
        // ============================

        private async Task InitializeChatClient()
        {
            // 1. Create 1 cancellation token source for 1 client
            cts = new CancellationTokenSource();

            // 2. Client create a client and server socket 
            serverEndpoint = new IPEndPoint(NetworkingHelper.SERVER_IPADDRESS, NetworkingHelper.SERVER_PORT);
            clientSocket = new Socket(serverEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // 3. Connect clientSocket to the serverEndpoint
            await clientSocket.ConnectAsync(serverEndpoint);

            // 3. Create an thread to read message sending from server
            _ = ReceiveMessagesAsync(cts.Token);
        }

        /// <summary>
        /// Receive messages from server
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var receiveBuffer = new byte[4096];

            // Looping and wait for the response
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var byteReceivedLen = await clientSocket.ReceiveAsync(receiveBuffer);
                    if (byteReceivedLen == 0)
                    {
                        PrintMsgToTextBox("Invalid protocol!", false);
                    }
                    else
                    {
                        string textReceived = Encoding.UTF8.GetString(receiveBuffer, 0, byteReceivedLen);

                        // check file
                        if (textReceived.Contains("FILE:"))
                        {

                            // Parse header
                            var parts = textReceived.Split(':');
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
                        }
                        else
                        {
                            PrintMsgToTextBox(textReceived, false);
                        }
                    }
                }

                // If socket raises errors
                catch (SocketException ex)
                {
                    PrintMsgToTextBox($"Socket error: {ex.Message}", false);
                    break;
                }

                // If raise any related exceptions
                catch (Exception ex)
                {
                    PrintMsgToTextBox($"Socket error: {ex.Message}", false);
                    break;
                }
            }
        }

        /// <summary>
        /// Handle send message from client to server
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var text = txtBoxInputChat.Text;
            var textInBytes = Encoding.UTF8.GetBytes(text);

            try
            {
                await clientSocket.SendAsync(textInBytes);

                // Send the <EXIT> prompt to server to disconnect client
                if (text.Equals(NetworkingHelper.CHAT_PROMPT_EXIT))
                {
                    await clientSocket.SendAsync(Encoding.UTF8.GetBytes(NetworkingHelper.CHAT_PROMPT_EXIT));
                }

                // Print Msg into text box
                PrintMsgToTextBox(text, true);
            }

            catch (SocketException ex)
            {
                PrintMsgToTextBox($"Socket error: {ex.Message}", false);
            }
            catch (Exception ex)
            {
                PrintMsgToTextBox($"Socket error: {ex.Message}", false);
            }
        }

        /// <summary>
        /// Print msg to the msg box 
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="isCurrentClient"></param>
        private void PrintMsgToTextBox(string msg, bool isCurrentClient = false, bool isFile = false, string fileName = null, byte[] fileData = null)
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
                    Text = isCurrentClient ? $"(Current Client) {msg}" : $"(Client) {msg}",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                msgContainer.Children.Add(lblMessage);

                // If file, then add download button
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

                // Add to main stackpanel
                chatPanel.Children.Add(msgContainer);
            });
        }

        private async void Button_AttachFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.ShowDialog();

            string filePath = fileDialog.FileName;
            string fileName = System.IO.Path.GetFileName(filePath);

            byte[] fileData = await File.ReadAllBytesAsync(filePath);
            string header = $"FILE:{fileName}:{fileData.Length}";

            byte[] headerBytes = Encoding.UTF8.GetBytes(header);

            try
            {
                await clientSocket.SendAsync(headerBytes, SocketFlags.None);
                await clientSocket.SendAsync(fileData, SocketFlags.None);

                // Print Msg into text box
                PrintMsgToTextBox($"Sent file: {fileName}", true, true, fileName, fileData);

            }
            catch (SocketException ex)
            {
                PrintMsgToTextBox($"Socket error: {ex.Message}", false);
            }
            catch (Exception ex)
            {
                PrintMsgToTextBox($"Socket error: {ex.Message}", false);
            }

        }

    } 
}
