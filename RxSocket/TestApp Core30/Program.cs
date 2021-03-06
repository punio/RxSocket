﻿using System;
using System.Net;
using System.Text;
using RxSocket;

namespace TestApp_Core30
{
	class Program
	{
		static void Main(string[] args)
		{
			var server = new RxTcpServer();
			server.Error.Subscribe(e => Warning($"[Server] error.({e.Method}) {e.Exception.Message}"));
			server.Accepted.Subscribe(c => Information($"[Server] Accept ({c.Client.RemoteEndPoint})"));
			server.Closed.Subscribe(e => Information($"[Server] Closed ({e})"));
			server.Received.Subscribe(t => Receive($"[Server] Received from {t.RemoteEndPoint} {t.Data.Length} bytes."));
			// Listen
			try
			{
				server.Listen(IPAddress.Any.ToString(), 12345);
				Console.WriteLine("Listen 12345");
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}


			var client = new RxTcpClient();
			// Subscribe
			client.Error.Subscribe(e => Warning($"[Client] error.({e.Method}) {e.Exception.Message}"));
			client.Closed.Subscribe(e => Information($"[Client] Closed ({e})"));
			client.Received.Subscribe(t => Receive($"[Client] Received {t.RemoteEndPoint} {t.Data.Length} bytes."));
			client.KeepAliveTime = 10 * 1000;
			client.KeepAliveInterval = 1000;
			client.KeepAliveRetryCount = 5;
			try
			{
				client.Connect("127.0.0.1", 12345);
				Console.WriteLine("Connect 127.0.0.1:12345");
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}


			Console.Write("Client >");
			var line = Console.ReadLine();

			// change keep alive
			client.SetKeepAliveTime(3);
			client.SetKeepAliveInterval(1);
			client.SetKeepAliveRetryCount(1);

			while (!string.IsNullOrEmpty(line))
			{
				client.Send(Encoding.UTF8.GetBytes(line));

				Console.Write("Server >");
				line = Console.ReadLine();

				if (string.IsNullOrEmpty(line)) break;
				server.Broadcast(Encoding.UTF8.GetBytes(line));

				Console.Write("Client >");
				line = Console.ReadLine();
			}

			client.Close();
			server.Close();

			Console.ReadKey();


		}

		static void Information(string log)
		{
			Console.WriteLine(log);
		}
		static void Warning(string log)
		{
			Console.ForegroundColor = ConsoleColor.Magenta;
			Console.WriteLine(log);
			Console.ResetColor();
		}
		static void Receive(string log)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine(log);
			Console.ResetColor();
		}
	}
}
