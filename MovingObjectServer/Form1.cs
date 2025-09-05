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

namespace MovingObjectServer
{
    public partial class Form1 : Form
    {
        private Socket serverSocket;
        private List<ClientInfo> connectedClients = new List<ClientInfo>();
        private byte[] buffer;

        Pen red = new Pen(Color.Red);
        Rectangle rect = new Rectangle(20, 20, 30, 30);
        SolidBrush fillBlue = new SolidBrush(Color.Blue);
        int slide = 10;
        private bool serverRunning = false;

        public Form1()
        {
            InitializeComponent();
            StartServer();
            timer1.Interval = 50;
            timer1.Enabled = true;

            // Update form title to show server status
            this.Text = "Moving Object Server - Listening on Port 1111";
            this.FormClosing += Form1_FormClosing;
        }

        private class ClientInfo
        {
            public Socket Socket { get; set; }
            public string ClientId { get; set; }
            public DateTime ConnectedTime { get; set; }
            public bool IsConnected { get; set; }

            public ClientInfo(Socket socket)
            {
                Socket = socket;
                ClientId = socket.RemoteEndPoint.ToString();
                ConnectedTime = DateTime.Now;
                IsConnected = true;
            }
        }

        private static void ShowErrorDialog(string message)
        {
            MessageBox.Show(message, "Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void StartServer()
        {
            try
            {
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                serverSocket.Bind(new IPEndPoint(IPAddress.Any, 1111));
                serverSocket.Listen(10);
                serverSocket.BeginAccept(AcceptCallback, null);
                serverRunning = true;

                Console.WriteLine($"[{DateTime.Now}] Server Started on port 1111...");
                Console.WriteLine($"[{DateTime.Now}] Waiting for client connections...");
            }
            catch (SocketException ex)
            {
                ShowErrorDialog($"Socket Error: {ex.Message}");
                serverRunning = false;
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"Server Start Error: {ex.Message}");
                serverRunning = false;
            }
        }

        private void AcceptCallback(IAsyncResult ar)
        {
            try
            {
                if (!serverRunning) return;

                Socket clientSocket = serverSocket.EndAccept(ar);
                ClientInfo newClient = new ClientInfo(clientSocket);

                lock (connectedClients)
                {
                    connectedClients.Add(newClient);
                }

                Console.WriteLine($"[{DateTime.Now}] New client connected: {newClient.ClientId}");
                Console.WriteLine($"[{DateTime.Now}] Total clients: {connectedClients.Count}");

                buffer = new byte[1024];
                clientSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, newClient);

                // Continue accepting new connections
                serverSocket.BeginAccept(AcceptCallback, null);

                // Send current position to new client immediately
                PushDataToClient(newClient, $"{rect.X},{rect.Y}");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Accept Error: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine($"[{DateTime.Now}] Server socket disposed during accept");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Unexpected Accept Error: {ex.Message}");
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            ClientInfo client = (ClientInfo)ar.AsyncState;
            try
            {
                int receivedData = client.Socket.EndReceive(ar);

                if (receivedData == 0)
                {
                    // Client disconnected gracefully
                    DisconnectClient(client, "Client disconnected gracefully");
                    return;
                }

                // Process received data if needed (for heartbeat, commands, etc.)
                string receivedMessage = Encoding.ASCII.GetString(buffer, 0, receivedData);
                Console.WriteLine($"[{DateTime.Now}] Received from {client.ClientId}: {receivedMessage.Trim()}");

                // Continue receiving from this client
                client.Socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, client);
            }
            catch (SocketException ex)
            {
                DisconnectClient(client, $"Socket error: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                DisconnectClient(client, "Socket disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Receive Error from {client.ClientId}: {ex.Message}");
            }
        }

        private void DisconnectClient(ClientInfo client, string reason)
        {
            try
            {
                client.IsConnected = false;
                client.Socket?.Close();

                lock (connectedClients)
                {
                    connectedClients.Remove(client);
                }

                Console.WriteLine($"[{DateTime.Now}] Client {client.ClientId} disconnected: {reason}");
                Console.WriteLine($"[{DateTime.Now}] Remaining clients: {connectedClients.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Error disconnecting client: {ex.Message}");
            }
        }

        private void PushDataToClient(ClientInfo client, string data)
        {
            try
            {
                if (!client.IsConnected || !client.Socket.Connected)
                    return;

                byte[] sendData = Encoding.ASCII.GetBytes(data);
                client.Socket.BeginSend(sendData, 0, sendData.Length, SocketFlags.None, SendCallback, client);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Error pushing data to {client.ClientId}: {ex.Message}");
                DisconnectClient(client, "Send error");
            }
        }

        private void PushDataToAllClients(string data)
        {
            List<ClientInfo> clientsToRemove = new List<ClientInfo>();

            lock (connectedClients)
            {
                foreach (ClientInfo client in connectedClients.ToList())
                {
                    if (!client.Socket.Connected)
                    {
                        clientsToRemove.Add(client);
                        continue;
                    }

                    PushDataToClient(client, data);
                }

                // Remove disconnected clients
                foreach (ClientInfo client in clientsToRemove)
                {
                    DisconnectClient(client, "Connection lost");
                }
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            ClientInfo client = (ClientInfo)ar.AsyncState;
            try
            {
                int sentBytes = client.Socket.EndSend(ar);
                // Data successfully sent
            }
            catch (SocketException ex)
            {
                DisconnectClient(client, $"Send error: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                DisconnectClient(client, "Socket disposed during send");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Send callback error: {ex.Message}");
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            // Update object position
            back();
            rect.X += slide;
            Invalidate(); // Redraw the form

            // Data Push: Actively push position data to all connected clients
            string positionData = $"{rect.X},{rect.Y}";
            PushDataToAllClients(positionData);

            // Update form title with client count
            this.Text = $"Moving Object Server - Clients Connected: {connectedClients.Count}";
        }

        private void back()
        {
            if (rect.X >= this.Width - rect.Width * 2)
                slide = -10;
            else if (rect.X <= rect.Width / 2)
                slide = 10;
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawRectangle(red, rect);
            g.FillRectangle(fillBlue, rect);

            // Draw server info
            using (Font font = new Font("Arial", 10))
            using (SolidBrush brush = new SolidBrush(Color.Black))
            {
                g.DrawString($"Server Status: {(serverRunning ? "Running" : "Stopped")}", font, brush, 10, this.Height - 60);
                g.DrawString($"Connected Clients: {connectedClients.Count}", font, brush, 10, this.Height - 40);
                g.DrawString($"Position: ({rect.X}, {rect.Y})", font, brush, 10, this.Height - 20);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            serverRunning = false;

            // Disconnect all clients
            lock (connectedClients)
            {
                foreach (ClientInfo client in connectedClients.ToList())
                {
                    try
                    {
                        client.Socket?.Close();
                    }
                    catch { }
                }
                connectedClients.Clear();
            }

            // Close server socket
            try
            {
                serverSocket?.Close();
            }
            catch { }

            Console.WriteLine($"[{DateTime.Now}] Server shutting down...");
        }
    }
}