using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Conduit
{
    class Program
    {
        private static List<Task> threads = new();
        private static Dictionary<string, string> ports = new();
        
        static async Task Main(string[] args)
        {
            string path = Environment.UserName != "root" ? "conduit.conf" : "/root/Conduit/conduit.conf";
            
            if (!File.Exists(path))
            {
                File.Create(path).Close();
                File.WriteAllText(path, "# Conduit Configuration File #\n\nALIAS conduit-test AS conduit-test.blockbase.gg:3139\n\nFORWARD 8888 TO conduit-test");
            }
            string[] lines = File.ReadAllLines(path);

            ReadWhitelist();

            Console.WriteLine("===================================");
            Dictionary<string, string> aliases = new();
            foreach (string line in lines)
            {
                if(string.IsNullOrEmpty(line) || line.StartsWith("#"))
                {
                    continue;
                }
                string[] parts = line.Split(' ');
                // ALIAS conduit-test AS conduit-test.blockbase.gg:3139
                // FORWARD 8888 TO conduit-test.blockbase.gg:3139
                string verb = parts[0];
                string local = parts[1];
                string remote = parts[3];

                if (verb == "FORWARD")
                {
                    int remotePort = remote.Split(':').Length > 1 ? int.Parse(remote.Split(':')[1]) : 0;
                    string remoteHost = remote.Split(':')[0];
                    if (!IsValidIPv4(remoteHost))
                    {
                        if (aliases.ContainsKey(remoteHost))
                        {
                            string newHost = aliases[remoteHost].Split(':')[0];
                            remotePort = int.Parse(aliases[remoteHost].Split(':')[1]);
                            remoteHost = newHost;
                        }
                    }
                    int localPort = int.Parse(local);
                    var listener = new TcpListener(IPAddress.Any, localPort);
                    listener.Start();
                    Console.WriteLine($"localhost:{localPort} -> {remoteHost}:{remotePort}");
                    Task task = new Task(async () =>
                    {
                        while (true)
                        {
                            var client = await listener.AcceptTcpClientAsync();
                            _ = HandleClientAsync(client, localPort, remoteHost, remotePort);
                        }
                    });
                    threads.Add(task);
                    task.Start();
                }
                else if (verb == "ALIAS")
                {
                    aliases.Add(local, remote);
                }
            }
            Console.WriteLine("===================================");
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey();
                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Q)
                {
                    Console.WriteLine("Received interrupt, exiting...");
                    foreach (Task task in threads)
                    {
                        task.Dispose();
                    }
                    Environment.Exit(0);
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.E)
                {
                    Console.Clear();
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client, int localPort, string remoteHost, int remotePort)
        {
            using (client)
            {
                string ip = client.Client.RemoteEndPoint.ToString().Split(':')[0];

                Console.Write(DateTime.Now.ToLongTimeString() + ": " + ip+":"+localPort+" -> "+remoteHost+":"+remotePort);
                ReadWhitelist();
                string logPath = Environment.UserName != "root" ? "log.txt" : "/root/Conduit/log.txt";
                if(!File.Exists(logPath)) File.Create(logPath).Close();
                if (!ports.ContainsKey(ip) || !ports[ip].Contains(localPort.ToString()))
                {
                    Console.WriteLine(" [DROPPED]");
                    File.WriteAllText(logPath, File.ReadAllText(logPath) +
                                               DateTime.Now.ToShortDateString() + " " +
                                               DateTime.Now.ToLongTimeString() + ": " + ip + ":" + localPort + " -> " +
                                               remoteHost + ":" +
                                               remotePort + " [DROPPED]\n");
                    return;
                }

                Console.WriteLine(" [ACCEPTED]");
                File.WriteAllText(logPath, File.ReadAllText(logPath) +
                                           DateTime.Now.ToShortDateString() + " " +
                                           DateTime.Now.ToLongTimeString() + ": " + ip + ":" + localPort + " -> " +
                                           remoteHost + ":" +
                                           remotePort + " [ACCEPTED]\n");
                var remoteClient = new TcpClient();
                await remoteClient.ConnectAsync(remoteHost, remotePort);

                using (remoteClient)
                {
                    var clientStream = client.GetStream();
                    var remoteStream = remoteClient.GetStream();

                    var clientToRemote = CopyStreamAsync(clientStream, remoteStream);
                    var remoteToClient = CopyStreamAsync(remoteStream, clientStream);

                    await Task.WhenAny(clientToRemote, remoteToClient);
                }
            }
        }

        private static async Task CopyStreamAsync(NetworkStream input, NetworkStream output)
        {
            byte[] buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await output.WriteAsync(buffer, 0, bytesRead);
            }
        }

        private static void ReadWhitelist()
        {
            ports = new();
            string path = Environment.UserName != "root" ? "whitelist.conf" : "/root/Conduit/whitelist.conf";
            if (!File.Exists(path))
            {
                File.Create(path).Close();
                File.WriteAllText(path,
                    "# Conduit Whitelist File #\n\nALIAS localhost AS 127.0.0.1\n\nALLOW 127.0.0.1 TO 8888");
            }

            List<string> whitelist = File.ReadAllLines(path).ToList();
            Dictionary<string, string> aliases = new();
            foreach (string entry in whitelist)
            {
                if (string.IsNullOrEmpty(entry) || entry.StartsWith("#"))
                {
                    continue;
                }
                
                // ALLOW 1.1.1.1 TO 8888
                string[] parts = entry.Split(' ');
                string verb = parts[0];
                string ip = parts[1];
                if (!IsValidIPv4(ip))
                {
                    if (aliases.ContainsValue(ip))
                    {
                        ip = aliases.Where(alias => alias.Value == ip).First().Key;
                    }
                }
                string port = parts[3];
                if (verb == "ALLOW")
                {
                    if (!ports.ContainsKey(ip))
                    {
                        ports.Add(ip, port);
                    }
                    else
                    {
                        ports[ip] += ", " + port;
                    }
                }
                else if (verb == "DENY")
                {
                    if (ports.ContainsKey(ip))
                    {
                        ports[ip] = ports[ip].Replace(port, "");
                    }
                }
                else if (verb == "ALIAS")
                {
                    aliases.Add(port, ip);
                }
            }
        }
        
        static bool IsValidIPv4(string ipString)
        {
            string ipv4Pattern = @"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$";
            return Regex.IsMatch(ipString, ipv4Pattern);
        }
    }
}