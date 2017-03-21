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

		public void Listen(int port, bool enableBroadcast = true, bool exclusiveAddressUse = false)
		{
			Close();
			_disposable = new CompositeDisposable();

			Client = new UdpClient();
			Client.EnableBroadcast = enableBroadcast;
			Client.ExclusiveAddressUse = exclusiveAddressUse;
			Client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
			_disposable.Add(Client.ReceiveAsync().ToObservable().Subscribe(r => _received.OnNext(new UdpData(r.RemoteEndPoint, r.Buffer))));

			IsMulticast = false;
		}

		public void Listen(string multicastAddress, int port, bool enableBroadcast = true, bool exclusiveAddressUse = false)
		{
			Listen(port, enableBroadcast, exclusiveAddressUse);
			MulticastAddress = IPAddress.Parse(multicastAddress);
			Client.JoinMulticastGroup(MulticastAddress);
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
