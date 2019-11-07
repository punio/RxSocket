using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace RxSocket
{
	public class RxTcpServer : IDisposable
	{
		#region Property
		public Socket Server { get; private set; }
		public ReadOnlyCollection<RxTcpClient> Clients { get; private set; }

		public bool EnableKeepAlive { get; set; } = true;   // Default Enable keep alive.
		public int KeepAliveTime { get; set; } = 30 * 60 * 1000;    // Default 30min
		public int KeepAliveInterval { get; set; } = 30000; // Default 30sec.
#if NETCORE3_0
		public int KeepAliveRetryCount { get; set; } = 1;   // Default 1.
#endif
		public int BufferSize { get; set; } = 1024; // Default 1k.
		#endregion

		#region Observable
		public IObservable<ErrorData> Error { get; }
		public IObservable<RxTcpClient> Accepted { get; }
		public IObservable<EndPoint> Closed { get; }
		public IObservable<TcpData> Received { get; }
		#endregion

		#region Field
		private readonly Subject<ErrorData> _error = new Subject<ErrorData>();
		private readonly Subject<RxTcpClient> _accepted = new Subject<RxTcpClient>();
		private readonly Subject<EndPoint> _closed = new Subject<EndPoint>();
		private readonly Subject<TcpData> _received = new Subject<TcpData>();
		private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new ConcurrentDictionary<string, ConnectedClient>();
		#endregion


		public RxTcpServer()
		{
			Error = _error.AsObservable();
			Accepted = _accepted.AsObservable();
			Closed = _closed.AsObservable();
			Received = _received.AsObservable();
			Clients = new ReadOnlyCollection<RxTcpClient>(_clients.Values.Select(c => c.Client).ToList());
		}

		public void Dispose() => this.Close();


		public void Listen(string localAddress, int port) => Listen(localAddress, port, (int)SocketOptionName.MaxConnections);
		public void Listen(string localAddress, int port, int backlog)
		{
			if (Server != null) Close();

			IPAddress address;
			if (!IPAddress.TryParse(localAddress, out address))
			{
				var hostEntry = Dns.GetHostEntry(address);
				if ((hostEntry.AddressList?.Length ?? 0) <= 0) throw new Exception("Invalid Address");
				address = hostEntry.AddressList[0];
			}
			var endPoint = new IPEndPoint(address, port);

			Server = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			Server.Bind(endPoint);
			Server.Listen(backlog);
			StartAccept();
		}

		/// <summary>
		/// Stop Listen
		/// </summary>
		public void Close()
		{
			Server?.Close();
			Server = null;
			CloseAllClients();
		}

		/// <summary>
		/// Close all connected clients.
		/// </summary>
		public void CloseAllClients()
		{
			foreach (var client in _clients)
			{
				client.Value.Dispose();
			}
			_clients.Clear();
			foreach (var client in Clients) client.Close();
			Clients = new ReadOnlyCollection<RxTcpClient>(_clients.Values.Select(c => c.Client).ToList());
		}

		/// <summary>
		/// Send to all clients
		/// </summary>
		/// <param name="data"></param>
		public void Broadcast(byte[] data)
		{
			foreach (var client in Clients) client.Send(data);
		}

		private void StartAccept()
		{
			try
			{
				Server.BeginAccept(AcceptCallback, null);
			}
			catch (Exception exp)
			{
				_error.OnNext(new ErrorData("BeginAccept", exp));
			}
		}

		private void AcceptCallback(IAsyncResult result)
		{
			if (Server == null) return;
			Socket client = null;
			try
			{
				client = Server.EndAccept(result);
			}
			catch (Exception exp)
			{
				_error.OnNext(new ErrorData("EndAccept", exp));
			}

			if (client != null)
			{
				var rxClient = new RxTcpClient(client)
				{
					EnableKeepAlive = EnableKeepAlive,
					KeepAliveTime = KeepAliveTime,
					KeepAliveInterval = KeepAliveInterval,
#if NETCORE3_0
					KeepAliveRetryCount = KeepAliveRetryCount,
#endif
					BufferSize = BufferSize // これだと初回が・・・
				};
				if (EnableKeepAlive) rxClient.SetKeepAlive();

				var connectedClient = new ConnectedClient { Client = rxClient };
				if (!_clients.TryAdd(connectedClient.Key, connectedClient))
				{
					// ???

				}
				Clients = new ReadOnlyCollection<RxTcpClient>(_clients.Values.Select(c => c.Client).ToList());
				_accepted.OnNext(rxClient); // Notice

				connectedClient.Disposable.Add(rxClient.Closed.Subscribe(ClientClosed));
				connectedClient.Disposable.Add(rxClient.Received.Subscribe(_received));
				connectedClient.Disposable.Add(rxClient.Error.Subscribe(_error));
			}

			StartAccept();
		}

		private void ClientClosed(EndPoint endpoint)
		{
			ConnectedClient listData;
			_clients.TryRemove(endpoint.ToString(), out listData);
			listData.Dispose();
			Clients = new ReadOnlyCollection<RxTcpClient>(_clients.Values.Select(c => c.Client).ToList());
			_closed.OnNext(endpoint);
		}

		class ConnectedClient : IDisposable
		{
			public string Key => Client?.Client?.RemoteEndPoint.ToString();

			public RxTcpClient Client { get; set; }
			public CompositeDisposable Disposable { get; } = new CompositeDisposable();

			public void Dispose() => Disposable.Dispose();
		}
	}
}
