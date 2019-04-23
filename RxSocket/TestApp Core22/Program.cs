using System;
using System.Net;
using System.Text;
using RxSocket;

namespace TestApp
{
	class Program
	{
		static void Main(string[] args)
		{
			var server = new RxTcpServer();
			server.Error.Subscribe(e => Console.WriteLine($"[Server] error.({e.Method}) {e.Exception.Message}"));
			server.Accepted.Subscribe(c => Console.WriteLine($"[Server] Accept ({c.Client.RemoteEndPoint})"));
			server.Closed.Subscribe(e => Console.WriteLine($"[Server] Closed ({e})"));
			server.Received.Subscribe(t => Console.WriteLine($"[Server] Received {t.Data.Length} bytes."));
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
			client.Error.Subscribe(e => Console.WriteLine($"[Client] error.({e.Method}) {e.Exception.Message}"));
			client.Closed.Subscribe(e => Console.WriteLine($"[Client] Closed ({e})"));
			client.Received.Subscribe(t => Console.WriteLine($"[Client] Received {t.Data.Length} bytes."));

			// Connect
			try
			{
				client.Connect("127.0.0.1", 12344);
				Console.WriteLine("Connect 127.0.0.1:12344");
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
			try
			{
				client.Connect("127.0.0.1", 12345);
				Console.WriteLine("Connect 127.0.0.1:12345");
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}

			Console.Write(">");
			var line = Console.ReadLine();
			while (!string.IsNullOrEmpty(line))
			{
				client.Send(Encoding.UTF8.GetBytes(line));
				Console.Write(">");
				line = Console.ReadLine();
			}

			client.Close();
			server.Close();

			Console.ReadKey();
		}
	}
}
