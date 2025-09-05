using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MovingObjectClient
{
    public partial class Form1 : Form
    {
        private Socket clientSocket;
        private byte[] buffer;
        Pen red = new Pen(Color.Red);
        Rectangle rect = new Rectangle(20, 20, 30, 30);
        SolidBrush fillBlue = new SolidBrush(Color.Blue);

        private bool isConnected = false;
        private string serverIP;
        private int serverPort = 1111;
        private DateTime lastDataReceived = DateTime.Now;

        public Form1()
        {
            InitializeComponent();

            GetServerIP();

            StartConnect();

            timer1.Interval = 1000; 
            timer1.Enabled = true;

            this.Text = "Moving Object Client - Connecting...";
            this.FormClosing += Form1_FormClosing;
        }

        private void GetServerIP()
        {
            try
            {
                var ipHost = Dns.GetHostAddresses(Dns.GetHostName());
                serverIP = ipHost.First(a => a.AddressFamily == AddressFamily.InterNetwork).ToString();

                

                Console.WriteLine($"[{DateTime.Now}] Connecting to server: {serverIP}:{serverPort}");
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"Error getting server IP: {ex.Message}");
                serverIP = "127.0.0.1"; // Default to localhost
            }
        }

        private static void ShowErrorDialog(string message)
        {
            MessageBox.Show(message, "Client Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void StartConnect()
        {
            try
            {
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var endPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);
                clientSocket.BeginConnect(endPoint, ConnectCallback, null);

                Console.WriteLine($"[{DateTime.Now}] Client attempting to connect to {serverIP}:{serverPort}...");
            }
            catch (SocketException ex)
            {
                ShowErrorDialog($"Connection Error: {ex.Message}");
                isConnected = false;
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"Unexpected Error: {ex.Message}");
                isConnected = false;
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                clientSocket.EndConnect(ar);
                isConnected = true;
                lastDataReceived = DateTime.Now;

                buffer = new byte[1024];
                clientSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, null);

                Console.WriteLine($"[{DateTime.Now}] Client successfully connected to server");
                this.Invoke((Action)(() => this.Text = $"Moving Object Client - Connected to {serverIP}"));

                SendToServer("CLIENT_CONNECTED");
            }
            catch (SocketException ex)
            {
                isConnected = false;
                ShowErrorDialog($"Connection failed: {ex.Message}");
                Console.WriteLine($"[{DateTime.Now}] Connection failed: {ex.Message}");

                Timer reconnectTimer = new Timer();
                reconnectTimer.Interval = 5000; 
                reconnectTimer.Tick += (s, e) =>
                {
                    reconnectTimer.Stop();
                    reconnectTimer.Dispose();
                    if (!isConnected)
                    {
                        Console.WriteLine($"[{DateTime.Now}] Attempting to reconnect...");
                        StartConnect();
                    }
                };
                reconnectTimer.Start();
            }
            catch (Exception ex)
            {
                isConnected = false;
                ShowErrorDialog($"Connection callback error: {ex.Message}");
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                int received = clientSocket.EndReceive(ar);

                if (received > 0)
                {
                    lastDataReceived = DateTime.Now;

                    string message = Encoding.ASCII.GetString(buffer, 0, received).Trim();
                    ProcessReceivedData(message);

                    clientSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, null);
                }
                else
                {
                    HandleDisconnection("Server closed connection");
                }
            }
            catch (SocketException ex)
            {
                HandleDisconnection($"Socket error: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                HandleDisconnection("Socket disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Receive error: {ex.Message}");
                HandleDisconnection($"Receive error: {ex.Message}");
            }
        }

        private void ProcessReceivedData(string data)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(data)) return;

                string[] parts = data.Split(',');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                    {
                        this.Invoke((Action)(() =>
                        {
                            rect.X = x;
                            rect.Y = y;
                            Invalidate(); 
                        }));

                        Console.WriteLine($"[{DateTime.Now}] Position updated: ({x}, {y})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Error processing data '{data}': {ex.Message}");
            }
        }

        private void SendToServer(string message)
        {
            try
            {
                if (isConnected && clientSocket?.Connected == true)
                {
                    byte[] data = Encoding.ASCII.GetBytes(message);
                    clientSocket.BeginSend(data, 0, data.Length, SocketFlags.None, SendCallback, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Error sending to server: {ex.Message}");
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                int bytesSent = clientSocket.EndSend(ar);
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Send error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Send callback error: {ex.Message}");
            }
        }

        private void HandleDisconnection(string reason)
        {
            isConnected = false;

            Console.WriteLine($"[{DateTime.Now}] Disconnected: {reason}");

            this.Invoke((Action)(() =>
            {
                this.Text = "Moving Object Client - Disconnected";
            }));

            try
            {
                clientSocket?.Close();
            }
            catch { }

            Timer reconnectTimer = new Timer();
            reconnectTimer.Interval = 3000; 
            reconnectTimer.Tick += (s, e) =>
            {
                reconnectTimer.Stop();
                reconnectTimer.Dispose();
                if (!isConnected)
                {
                    Console.WriteLine($"[{DateTime.Now}] Attempting to reconnect...");
                    this.Invoke((Action)(() => this.Text = "Moving Object Client - Reconnecting..."));
                    StartConnect();
                }
            };
            reconnectTimer.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (isConnected)
            {
                TimeSpan timeSinceLastData = DateTime.Now - lastDataReceived;
                if (timeSinceLastData.TotalSeconds > 10) 
                {
                    Console.WriteLine($"[{DateTime.Now}] No data received for {timeSinceLastData.TotalSeconds} seconds");
                    HandleDisconnection("Data timeout");
                }
                else
                {
                    SendToServer("HEARTBEAT");
                }
            }

            this.Invoke((Action)(() =>
            {
                if (isConnected)
                {
                    this.Text = $"Moving Object Client - Connected | Position: ({rect.X}, {rect.Y})";
                }
                else
                {
                    this.Text = "Moving Object Client - Disconnected";
                }
            }));
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawRectangle(red, rect);
            g.FillRectangle(fillBlue, rect);

            using (Font font = new Font("Arial", 10))
            using (SolidBrush brush = new SolidBrush(isConnected ? Color.Green : Color.Red))
            {
                string status = isConnected ? "Connected" : "Disconnected";
                g.DrawString($"Status: {status}", font, brush, 10, this.Height - 80);

                using (SolidBrush blackBrush = new SolidBrush(Color.Black))
                {
                    g.DrawString($"Server: {serverIP}:{serverPort}", font, blackBrush, 10, this.Height - 60);
                    g.DrawString($"Position: ({rect.X}, {rect.Y})", font, blackBrush, 10, this.Height - 40);
                    g.DrawString($"Last Data: {lastDataReceived:HH:mm:ss}", font, blackBrush, 10, this.Height - 20);
                }
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            isConnected = false;

            try
            {
                SendToServer("CLIENT_DISCONNECTING");
                System.Threading.Thread.Sleep(100); 
            }
            catch { }

            try
            {
                clientSocket?.Close();
            }
            catch { }

            Console.WriteLine($"[{DateTime.Now}] Client shutting down...");
        }
    }
}