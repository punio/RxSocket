using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using RxSocket;

namespace TestApp
{
	class Program
	{
		static void Main(string[] args)
		{
			var nicInformations = NetworkInterfaceCardInformation.GetNetworkInterfaceCardInformations();
			if (nicInformations != null)
			{
				// List current network informations
				foreach (var nic in nicInformations) Console.WriteLine(nic.ToMultiLineString());

				#region Multicast receive (IPv6)
				var multicastTargetNicV6 = nicInformations.FirstOrDefault(nic => nic.SupportsMulticast && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback && nic.IsSupportIPv6);
				if (multicastTargetNicV6 != null)
				{
					Console.WriteLine($"Listen multicast(IPv6) data.(NIC:{multicastTargetNicV6.IPv6Property.Index})");
					var udpListenerV6 = new RxUdpListener();
					udpListenerV6.Received.Subscribe(t => Receive($"[UDP] Received from {t.From} {t.Data.Length} bytes."));
					udpListenerV6.Listen("FF02::1", multicastTargetNicV6.IPv6Property.Index, 5000);
				}
				#endregion

				#region Multicast receive(IPv4)
				var multicastTargetNicV4 = nicInformations.FirstOrDefault(nic => nic.SupportsMulticast && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback && nic.IsSupportIPv4 && nic.UnicastAddresses.Any(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork));
				if (multicastTargetNicV4 != null)
				{
					var unicastAddress = multicastTargetNicV4.UnicastAddresses.First(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
					Console.WriteLine($"Listen multicast(IPv4) data.(NIC:{unicastAddress})");
					var udpListenerV4 = new RxUdpListener();
					udpListenerV4.Received.Subscribe(t => Receive($"[UDP] Received from {t.From} {t.Data.Length} bytes."));
					udpListenerV4.Listen("239.192.100.200", unicastAddress.Address.ToString(), 5001);
				}
				#endregion
			}

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

			// Connect
			try
			{
				client.Connect("127.0.0.1", 12344);
				Console.WriteLine("Connect 127.0.0.1:12344");
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			try
			{
				client.Connect("127.0.0.1", 12345);
				Console.WriteLine("Connect 127.0.0.1:12345");
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}

			Console.Write(">");
			var line = Console.ReadLine();
			while (!string.IsNullOrEmpty(line))
			{
				client.Send(Encoding.UTF8.GetBytes(line));

				Console.Write(">");
				line = Console.ReadLine();

				if (string.IsNullOrEmpty(line)) break;
				server.Broadcast(Encoding.UTF8.GetBytes(line));

				Console.Write(">");
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
