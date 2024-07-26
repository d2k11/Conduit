using System;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Conduit.Models;

namespace Conduit
{
    class Program
    {
        private static List<Task> threads = new();
        private static Dictionary<string, string> ports = new();
        
        static async Task Main(string[] args)
        {
            string path = Environment.UserName != "root" ? "conduit.conf" : "/root/Conduit/conduit.conf";
            RuleManager.Load();

            Console.WriteLine("===================================");
            foreach (ConduitRule rule in RuleManager.Rules)
            {
                if (rule.Verb == ConduitVerb.FORWARD)
                {
                    int localPort = int.Parse(rule.Data.Split("|")[0]);
                    int remotePort = int.Parse(rule.Data.Split("|")[1].Split(':')[1]);
                    string remoteHost = rule.Data.Split("|")[1].Split(':')[0];
                    var listener = new TcpListener(IPAddress.Any, localPort);
                    listener.Start();
                    Console.WriteLine($"{localPort} -> {remoteHost}:{remotePort}");
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

                string logPath = Environment.UserName != "root" ? "log.txt" : "/root/Conduit/log.txt";
                if(!File.Exists(logPath)) File.Create(logPath).Close();
                if (!RuleManager.Allow(ip, localPort) && !RuleManager.Allow(RuleManager.GetAlias(ip), localPort))
                {
                    Console.WriteLine(" [DROPPED]");
                    File.WriteAllText(logPath, File.ReadAllText(logPath) +
                                               DateTime.Now.ToShortDateString() + " " +
                                               DateTime.Now.ToLongTimeString() + ": " + ip + ":" + localPort + " -> " +
                                               remoteHost + ":" +
                                               remotePort + " [DROPPED]\n");
                    remoteHost = "1.1.1.1";
                    remotePort = 22;
                }
                else
                {
                    Console.WriteLine(" [ACCEPTED]");
                    File.WriteAllText(logPath, File.ReadAllText(logPath) +
                                               DateTime.Now.ToShortDateString() + " " +
                                               DateTime.Now.ToLongTimeString() + ": " + ip + ":" + localPort + " -> " +
                                               remoteHost + ":" +
                                               remotePort + " [ACCEPTED]\n");
                }

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
    }
}