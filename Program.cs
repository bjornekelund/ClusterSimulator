﻿using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClusterSimulator
{
    public class TelnetServer
    {
        private readonly TcpListener listener;
        private bool isRunning;
        private const int PORT = 2323;

        public TelnetServer()
        {
            listener = new TcpListener(IPAddress.Any, PORT);
        }

        public void Start()
        {
            listener.Start();
            isRunning = true;

            Console.WriteLine($"Telnet server started on port {PORT}");
            Console.WriteLine("Connect with: telnet localhost 2323");
            Console.WriteLine("Press Ctrl+C to stop the server");

            while (isRunning)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Console.WriteLine($"Client connected from: {client.Client.RemoteEndPoint}");

                    // Handle each client in a separate thread
                    Thread clientThread = new(() => HandleClient(client)) { IsBackground = true };
                    clientThread.Start();
                }
                catch (Exception ex)
                {
                    if (isRunning) // Only show error if we're supposed to be running
                    {
                        Console.WriteLine($"Error accepting client: {ex.Message}");
                    }
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try
            {
                Thread.Sleep(500);
                // Send telnet negotiation commands
                SendTelnetNegotiation(stream);

                // Send welcome message
                SendMessage(stream, "Welcome to Simple Telnet Server!\r\n");
                SendMessage(stream, "Available commands: help, time, echo <message>, bye\r\n");
                SendMessage(stream, "> ");

                Thread.Sleep(1000);

                while (client.Connected && isRunning)
                {
                    Random random = new();
                    string suffix = new([.. Enumerable.Range(0, 3).Select(_ => (char)random.Next('A', 'Z' + 1))]);
                    string number = ((char)random.Next('0', '9' + 1)).ToString();
                    string prefix = new([.. Enumerable.Range(0, 2).Select(_ => (char)random.Next('A', 'Z' + 1))]);
                    string mycall = "SM7IUN";
                    string spotter = $"{prefix}{number}{suffix}-#:";
                    string frequency = "14043.2";
                    string db = random.Next(11, 37).ToString() + " dB";
                    string wpm = random.Next(28, 45).ToString() + " WPM";
                    string comment = $"CW {db} {wpm} CQ";
                    string time = DateTime.UtcNow.ToString("HHmmZ");

                    // AK1A format
                    //           1         2         3         4         5         6         7
                    // 012345678901234567890123456789012345678901234567890123456789012345678901234
                    // DX de W3OA-#:     7031.5  W8KJP        CW 12 dB 22 WPM CQ           ? 1945Z
                    // DX de NN5ABC-#: 14065.00  SM8NIO       CW                             1844Z
                    // DX de PY2MKU-#   14065.0  SM7IUN       CW   29dB Q:9* Z:14,15,20      1922Z

                    Thread.Sleep(125);

                    SendMessage(stream, $"DX de {spotter,-9} {frequency,-8} {mycall,-13}{comment,-31}{time}\r\n");

                    if (stream.DataAvailable)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break; // Client disconnected

                        string input = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                        // Handle telnet control characters
                        input = CleanTelnetInput(input);

                        if (string.IsNullOrEmpty(input))
                        {
                            SendMessage(stream, "> ");
                            continue;
                        }

                        Console.WriteLine($"Received: {input}");

                        // Process the command
                        string response = ProcessCommand(input);

                        if (input.Equals("bye", StringComparison.OrdinalIgnoreCase))
                        {
                            SendMessage(stream, "Goodbye!\r\n");
                            break;
                        }

                        SendMessage(stream, response + "\r\n> ");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client error: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"Client disconnected: {client.Client?.RemoteEndPoint}");
                client.Close();
            }
        }

        private static void SendTelnetNegotiation(NetworkStream stream)
        {
            // Basic telnet negotiation to make clients happy
            // IAC WILL ECHO - Server will echo characters
            byte[] willEcho = [0xFF, 0xFB, 0x01];
            stream.Write(willEcho, 0, willEcho.Length);

            // IAC WILL SUPPRESS-GO-AHEAD - Suppress go-ahead
            byte[] willSuppressGA = [0xFF, 0xFB, 0x03];
            stream.Write(willSuppressGA, 0, willSuppressGA.Length);

            stream.Flush();
        }

        private static string CleanTelnetInput(string input)
        {
            // Remove common telnet control sequences and non-printable characters
            StringBuilder cleaned = new();

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                // Skip telnet IAC sequences (starts with 0xFF)
                if (c == 0xFF && i + 2 < input.Length)
                {
                    i += 2; // Skip the next two bytes
                    continue;
                }

                // Keep printable ASCII characters and common whitespace
                if (c >= 32 && c <= 126 || c == '\t' || c == '\r' || c == '\n')
                {
                    cleaned.Append(c);
                }
            }

            return cleaned.ToString().Trim();
        }

        private static string ProcessCommand(string command)
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Type 'help' for available commands";

            string cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "help":
                    return "Available commands:\r\n" +
                           "  help           - Show this help message\r\n" +
                           "  time           - Show current server time\r\n" +
                           "  echo <message> - Echo back your message\r\n" +
                           "  uptime         - Show server uptime\r\n" +
                           "  bye            - Disconnect from server";

                case "time":
                    return $"Current server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                case "uptime":
                    TimeSpan uptime = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
                    return $"Server uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";

                case "echo":
                    if (parts.Length > 1)
                    {
                        return "Echo: " + string.Join(" ", parts, 1, parts.Length - 1);
                    }
                    return "Echo: (no message provided)";

                default:
                    return $"Unknown command: '{command}'. Type 'help' for available commands.";
            }
        }

        private static void SendMessage(NetworkStream stream, string message)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }

        public void Stop()
        {
            isRunning = false;
            listener?.Stop();
            Console.WriteLine("Server stopped.");
        }

        public static void Main()
        {
            TelnetServer server = new();

            // Handle Ctrl+C gracefully
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                server.Stop();
                Environment.Exit(0);
            };

            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
    }
}