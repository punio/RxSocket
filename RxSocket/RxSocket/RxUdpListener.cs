using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;

namespace RxSocket
{
	public class RxUdpListener : IDisposable
	{
		#region Property
		public UdpClient Client { get; private set; }

		/// <summary>
		/// If this value is specified, then the listen method at Unicast binds to the specified local address.
		/// </summary>
		public IPAddress LocalAddress { get; set; } = IPAddress.None;

		public bool IsMulticast { get; private set; }
		public IPAddress MulticastAddress { get; private set; }
		#endregion

		#region Observable
		public IObservable<UdpData> Received { get; }
		#endregion

		#region Field
		private readonly Subject<UdpData> _received = new Subject<UdpData>();
		private CompositeDisposable _disposable;
		#endregion

		public RxUdpListener()
		{
			Received = _received.AsObservable();
		}

		public void Dispose() => this.Close();

		public void Listen(int port, bool enableBroadcast = true, bool exclusiveAddressUse = false, bool ipV6 = false)
		{
			Close();
			_disposable = new CompositeDisposable();

			if (ipV6)
			{
				Client = new UdpClient(AddressFamily.InterNetworkV6);
			}
			else
			{
				Client = new UdpClient();
			}

			Client.EnableBroadcast = enableBroadcast;
			Client.ExclusiveAddressUse = exclusiveAddressUse;
			if (!exclusiveAddressUse) Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			Client.Client.Bind(new IPEndPoint(LocalAddress.Equals(IPAddress.None) ? (ipV6 ? IPAddress.IPv6Any : IPAddress.Any) : LocalAddress, port));
			_disposable.Add(
				Observable.Defer(() => Client.ReceiveAsync().ToObservable())
					.Repeat()
					.Subscribe(r => _received.OnNext(new UdpData(r.RemoteEndPoint, r.Buffer)))
				);

			IsMulticast = false;
		}

		/// <summary>
		/// Wait for multicast data using the default NIC
		/// </summary>
		/// <param name="multicastAddress"></param>
		/// <param name="port"></param>
		/// <param name="enableBroadcast"></param>
		/// <param name="exclusiveAddressUse"></param>
		public void Listen(string multicastAddress, int port, bool enableBroadcast = true, bool exclusiveAddressUse = false)
		{
			MulticastAddress = IPAddress.Parse(multicastAddress);
			Listen(port, enableBroadcast, exclusiveAddressUse, MulticastAddress.AddressFamily == AddressFamily.InterNetworkV6);
			Client.JoinMulticastGroup(MulticastAddress);
			IsMulticast = true;
		}

		/// <summary>
		/// Wait for multicast data using local address NIC
		/// </summary>
		/// <param name="multicastAddress"></param>
		/// <param name="localAddress"></param>
		/// <param name="port"></param>
		/// <param name="enableBroadcast"></param>
		/// <param name="exclusiveAddressUse"></param>
		public void Listen(string multicastAddress, string localAddress, int port, bool enableBroadcast = true, bool exclusiveAddressUse = false)
		{
			MulticastAddress = IPAddress.Parse(multicastAddress);
			Listen(port, enableBroadcast, exclusiveAddressUse, MulticastAddress.AddressFamily == AddressFamily.InterNetworkV6);
			var localIpAddress = IPAddress.Parse(localAddress);
			Client.JoinMulticastGroup(MulticastAddress, localIpAddress);
			IsMulticast = true;
		}

		/// <summary>
		/// Wait for multicast data using specified index of NIC
		/// </summary>
		/// <param name="multicastAddress"></param>
		/// <param name="nicIndex"></param>
		/// <param name="port"></param>
		/// <param name="enableBroadcast"></param>
		/// <param name="exclusiveAddressUse"></param>
		/// <exception cref="SocketException">This method is only IPv6</exception>
		public void Listen(string multicastAddress, int nicIndex, int port, bool enableBroadcast = true, bool exclusiveAddressUse = false)
		{
			MulticastAddress = IPAddress.Parse(multicastAddress);
			if (MulticastAddress.AddressFamily != AddressFamily.InterNetworkV6) throw new SocketException((int)SocketError.OperationNotSupported);

			Listen(port, enableBroadcast, exclusiveAddressUse, true);

			Client.JoinMulticastGroup(nicIndex, MulticastAddress);
			IsMulticast = true;
		}

		public void Close()
		{
			_disposable?.Dispose();
			if (IsMulticast) Client?.DropMulticastGroup(MulticastAddress);
			Client?.Close();
			Client = null;
		}
	}
}
