using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;

namespace RxSocket
{
	public class RxTcpServer : IDisposable
	{
		#region Property
		public Socket Server => Volatile.Read(ref _session)?.Socket;
		public ReadOnlyCollection<RxTcpClient> Clients
		{
			get
			{
				var session = Volatile.Read(ref _session);
				return session?.Clients.Keys.ToList().AsReadOnly() ?? EmptyClients;
			}
		}

		public bool EnableKeepAlive { get; set; } = true;   // Default Enable keep alive.
		public int KeepAliveTime { get; set; } = 30 * 60 * 1000;    // Default 30min
		public int KeepAliveInterval { get; set; } = 30000; // Default 30sec.
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
		private readonly ISubject<TcpData> _received = Subject.Synchronize(new Subject<TcpData>());
		private static readonly ReadOnlyCollection<RxTcpClient> EmptyClients = Array.AsReadOnly(Array.Empty<RxTcpClient>());
		private ListeningSession _session;
		#endregion


		public RxTcpServer()
		{
			Error = _error.AsObservable();
			Accepted = _accepted.AsObservable();
			Closed = _closed.AsObservable();
			Received = _received.AsObservable();
		}

		public void Dispose() => this.Close();


		public void Listen(string localAddress, int port) => Listen(localAddress, port, (int)SocketOptionName.MaxConnections);
		public void Listen(string localAddress, int port, int backlog)
		{
			Close();

			if (!IPAddress.TryParse(localAddress, out var address))
			{
				var hostEntry = Dns.GetHostEntry(localAddress);
				if ((hostEntry.AddressList?.Length ?? 0) <= 0) throw new Exception("Invalid Address");
				address = hostEntry.AddressList[0];
			}
			var endPoint = new IPEndPoint(address, port);

			var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			try
			{
				socket.Bind(endPoint);
				socket.Listen(backlog);
			}
			catch
			{
				socket.Dispose();
				throw;
			}

			var session = new ListeningSession(socket);
			try
			{
				Interlocked.Exchange(ref _session, session)?.Dispose();
			}
			finally
			{
				StartAccept(session);
			}
		}

		/// <summary>
		/// Stop Listen
		/// </summary>
		public void Close() => Interlocked.Exchange(ref _session, null)?.Dispose();

		/// <summary>
		/// Close all connected clients.
		/// </summary>
		public void CloseAllClients() => Volatile.Read(ref _session)?.CloseAllClients();

		/// <summary>
		/// Send to all clients
		/// </summary>
		/// <param name="data"></param>
		public void Broadcast(byte[] data)
		{
			foreach (var client in Clients) client.Send(data);
		}

		private bool IsListening(ListeningSession session) => ReferenceEquals(Volatile.Read(ref _session), session);

		private void StartAccept(ListeningSession session)
		{
			if (!IsListening(session)) return;

			try
			{
				session.Socket.BeginAccept(AcceptCallback, session);
			}
			catch (Exception exp)
			{
				if (IsListening(session)) _error.OnNext(new ErrorData("BeginAccept", exp));
			}
		}

		private void AcceptCallback(IAsyncResult result)
		{
			var session = (ListeningSession)result.AsyncState;
			try
			{
				Socket socket;
				try
				{
					socket = session.Socket.EndAccept(result);
				}
				catch (Exception exp)
				{
					if (IsListening(session)) _error.OnNext(new ErrorData("EndAccept", exp));
					return;
				}

				RegisterClient(session, socket);
			}
			finally
			{
				StartAccept(session);
			}
		}

		private void RegisterClient(ListeningSession session, Socket socket)
		{
			RxTcpClient client = null;
			var subscriptions = new CompositeDisposable();
			var keepClient = false;
			Exception failure = null;
			try
			{
				if (!IsListening(session)) return;

				client = new RxTcpClient(socket, startReceiving: false)
				{
					EnableKeepAlive = EnableKeepAlive,
					KeepAliveTime = KeepAliveTime,
					KeepAliveInterval = KeepAliveInterval,
					BufferSize = BufferSize
				};
				if (client.EnableKeepAlive) client.SetKeepAlive();

				subscriptions.Add(client.Closed.Subscribe(endpoint => ClientClosed(session, client, endpoint)));
				subscriptions.Add(client.Received.Subscribe(_received));
				subscriptions.Add(client.Error.Subscribe(_error));
				session.Clients[client] = subscriptions;

				if (!IsListening(session) || !client.IsConnect) return;

				_accepted.OnNext(client);
				client.StartReceive(socket);
				keepClient = true;
			}
			catch (Exception exp)
			{
				failure = exp;
			}
			finally
			{
				if (!keepClient)
				{
					if (client != null) session.Clients.TryRemove(client, out _);
					subscriptions.Dispose();
					if (client == null) socket.Dispose();
					else client.Close();
				}
			}

			if (failure != null && IsListening(session)) _error.OnNext(new ErrorData("AcceptClient", failure));
		}

		private void ClientClosed(ListeningSession session, RxTcpClient client, EndPoint endpoint)
		{
			if (!session.Clients.TryRemove(client, out var subscriptions)) return;

			subscriptions.Dispose();
			_closed.OnNext(endpoint);
		}

		private sealed class ListeningSession : IDisposable
		{
			public ListeningSession(Socket socket)
			{
				Socket = socket;
			}

			public Socket Socket { get; }
			public ConcurrentDictionary<RxTcpClient, CompositeDisposable> Clients { get; } =
				new ConcurrentDictionary<RxTcpClient, CompositeDisposable>();

			public void CloseAllClients()
			{
				foreach (var client in Clients.Keys)
				{
					if (!Clients.TryRemove(client, out var subscriptions)) continue;

					subscriptions.Dispose();
					client.Close();
				}
			}

			public void Dispose()
			{
				try
				{
					Socket.Dispose();
				}
				finally
				{
					CloseAllClients();
				}
			}
		}
	}
}
