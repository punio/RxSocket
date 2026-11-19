using System;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading;

namespace RxSocket
{
	public class RxUdpListener : IDisposable
	{
		#region Property
		public UdpClient Client => Volatile.Read(ref _session)?.Client;

		/// <summary>
		/// If this value is specified, then the listen method at Unicast binds to the specified local address.
		/// </summary>
		public IPAddress LocalAddress { get; set; } = IPAddress.None;

		public bool IsMulticast => MulticastAddress != null;
		public IPAddress MulticastAddress => Volatile.Read(ref _session)?.MulticastAddress;
		#endregion

		#region Observable
		public IObservable<UdpData> Received { get; }

		/// <summary>
		/// Reports asynchronous receive failures after the failed listener has been stopped.
		/// </summary>
		public IObservable<ErrorData> Error { get; }
		#endregion

		#region Field
		private readonly Subject<UdpData> _received = new Subject<UdpData>();
		private readonly ISubject<ErrorData> _error = Subject.Synchronize(new Subject<ErrorData>());
		private ListeningSession _session;
		#endregion

		public RxUdpListener()
		{
			Received = _received.AsObservable();
			Error = _error.AsObservable();
		}

		public void Dispose() => this.Close();

		public void Listen(int port, bool enableBroadcast = true, bool exclusiveAddressUse = false, bool ipV6 = false)
		{
			var addressFamily = ipV6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
			StartListening(port, enableBroadcast, exclusiveAddressUse, addressFamily);
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
			var group = IPAddress.Parse(multicastAddress);
			StartListening(port, enableBroadcast, exclusiveAddressUse, group.AddressFamily, group,
				client => client.JoinMulticastGroup(group));
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
			var group = IPAddress.Parse(multicastAddress);
			var localIpAddress = IPAddress.Parse(localAddress);
			StartListening(port, enableBroadcast, exclusiveAddressUse, group.AddressFamily, group,
				client => client.JoinMulticastGroup(group, localIpAddress));
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
			var group = IPAddress.Parse(multicastAddress);
			if (group.AddressFamily != AddressFamily.InterNetworkV6) throw new SocketException((int)SocketError.OperationNotSupported);

			StartListening(port, enableBroadcast, exclusiveAddressUse, group.AddressFamily, group,
				client => client.JoinMulticastGroup(nicIndex, group));
		}

		public void Close() => Interlocked.Exchange(ref _session, null)?.Dispose();

		private void StartListening(int port, bool enableBroadcast, bool exclusiveAddressUse, AddressFamily addressFamily,
			IPAddress multicastAddress = null, Action<UdpClient> joinMulticastGroup = null)
		{
			Close();
			var session = new ListeningSession(new UdpClient(addressFamily), multicastAddress);
			var client = session.Client;
			try
			{
				client.EnableBroadcast = enableBroadcast;
				client.ExclusiveAddressUse = exclusiveAddressUse;
				if (!exclusiveAddressUse) client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

				var localAddress = LocalAddress;
				if (IPAddress.None.Equals(localAddress))
					localAddress = addressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
				client.Client.Bind(new IPEndPoint(localAddress, port));
				joinMulticastGroup?.Invoke(client);

				Interlocked.Exchange(ref _session, session)?.Dispose();
				session.ReceiveSubscription.Disposable = Observable.Defer(() => client.ReceiveAsync().ToObservable())
					.Repeat()
					.Subscribe(
						result => _received.OnNext(new UdpData(result.RemoteEndPoint, result.Buffer)),
						error => ReceiveFailed(session, error));
			}
			catch
			{
				Interlocked.CompareExchange(ref _session, null, session);
				session.Dispose();
				throw;
			}
		}

		private void ReceiveFailed(ListeningSession session, Exception error)
		{
			if (!ReferenceEquals(Interlocked.CompareExchange(ref _session, null, session), session)) return;

			try
			{
				session.Dispose();
			}
			finally
			{
				_error.OnNext(new ErrorData(nameof(UdpClient.ReceiveAsync), error));
			}
		}

		private sealed class ListeningSession : IDisposable
		{
			private int _disposed;

			public ListeningSession(UdpClient client, IPAddress multicastAddress)
			{
				Client = client;
				MulticastAddress = multicastAddress;
			}

			public UdpClient Client { get; }
			public IPAddress MulticastAddress { get; }
			public SingleAssignmentDisposable ReceiveSubscription { get; } = new SingleAssignmentDisposable();

			public void Dispose()
			{
				if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

				try
				{
					ReceiveSubscription.Dispose();
				}
				finally
				{
					// Closing the socket also drops its multicast memberships.
					Client.Dispose();
				}
			}
		}
	}
}
