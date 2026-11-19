using System;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace RxSocket
{
	public class RxTcpClient : IDisposable
	{
		#region Property
		public Socket Client => Volatile.Read(ref _client);
		public bool IsConnect { get; private set; }

		public EndPoint LocalEndPoint { get; private set; }
		public EndPoint RemoteEndPoint { get; private set; }

		public bool EnableKeepAlive { get; set; } = true;   // Default Enable keep alive.

		/// <summary>
		/// The KeepAliveTime specifies the timeout, in milliseconds, with no activity until the first keep-alive packet is sent.
		/// Default 30min
		/// </summary>
		public int KeepAliveTime { get; set; } = 30 * 60 * 1000;
		/// <summary>
		/// The KeepAliveInterval member specifies the interval, in milliseconds, between when successive keep-alive packets are sent if no acknowledgement is received
		/// Default 30sec.
		/// </summary>
		public int KeepAliveInterval { get; set; } = 30000;
		public int BufferSize { get; set; } = 1024; // Default 1k.
		#endregion

		#region Observable
		public IObservable<ErrorData> Error { get; }
		public IObservable<EndPoint> Closed { get; }
		public IObservable<TcpData> Received { get; }
		#endregion

		#region Field
		private readonly Subject<ErrorData> _error = new Subject<ErrorData>();
		private readonly Subject<EndPoint> _closed = new Subject<EndPoint>();
		private readonly Subject<TcpData> _received = new Subject<TcpData>();
		private readonly object _connectionGate = new object();
		private Socket _client;
		private object _connectAttempt;
		#endregion

		public RxTcpClient()
		{
			Error = _error.AsObservable();
			Closed = _closed.AsObservable();
			Received = _received.AsObservable();
		}

		public RxTcpClient(Socket socket) : this(socket, startReceiving: true)
		{
		}

		internal RxTcpClient(Socket socket, bool startReceiving) : this()
		{
			_client = socket;
			IsConnect = true;
			LocalEndPoint = socket.LocalEndPoint;
			RemoteEndPoint = socket.RemoteEndPoint;
			if (startReceiving) StartReceive(socket);
		}

		public void Dispose() => this.Close();


		public void Connect(string address, int port)
		{
			var attempt = BeginConnect();
			try
			{
				var addresses = IPAddress.TryParse(address, out var targetAddress)
					? new[] { targetAddress }
					: Dns.GetHostAddresses(address);
				ConnectToAddresses(addresses, port, attempt, (socket, endpoint) =>
				{
					socket.Connect(endpoint);
					return Task.CompletedTask;
				}).GetAwaiter().GetResult();
			}
			finally
			{
				EndConnect(attempt);
			}
		}

		public async Task ConnectAsync(string address, int port)
		{
			var attempt = BeginConnect();
			try
			{
				var addresses = IPAddress.TryParse(address, out var targetAddress)
					? new[] { targetAddress }
					: await Dns.GetHostAddressesAsync(address);
				await ConnectToAddresses(addresses, port, attempt, (socket, endpoint) => socket.ConnectAsync(endpoint));
			}
			finally
			{
				EndConnect(attempt);
			}
		}

		private object BeginConnect()
		{
			Close();
			lock (_connectionGate)
			{
				if (_client != null || _connectAttempt != null)
					throw new InvalidOperationException("Another connection is already active.");

				_connectAttempt = new object();
				return _connectAttempt;
			}
		}

		private void EndConnect(object attempt)
		{
			lock (_connectionGate)
			{
				if (ReferenceEquals(_connectAttempt, attempt)) _connectAttempt = null;
			}
		}

		private async Task ConnectToAddresses(IPAddress[] addresses, int port, object attempt, Func<Socket, EndPoint, Task> connect)
		{
			if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);

			ExceptionDispatchInfo lastError = null;
			foreach (var address in addresses)
			{
				var endpoint = new IPEndPoint(address, port);
				Socket socket = null;
				var connected = false;
				try
				{
					try
					{
						socket = CreateConnectingSocket(address.AddressFamily, attempt);
						if (EnableKeepAlive) SetKeepAlive(socket);
						await connect(socket, endpoint);
					}
					catch (SocketException exception)
					{
						lastError = ExceptionDispatchInfo.Capture(exception);
						continue;
					}

					FinishConnect(socket);
					connected = true;
					return;
				}
				finally
				{
					if (!connected && socket != null) Close(socket);
				}
			}

			lastError.Throw();
		}

		private Socket CreateConnectingSocket(AddressFamily addressFamily, object attempt)
		{
			lock (_connectionGate)
			{
				if (!ReferenceEquals(_connectAttempt, attempt)) throw new ObjectDisposedException(nameof(Socket));

				var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
				Volatile.Write(ref _client, socket);
				return socket;
			}
		}

		private void FinishConnect(Socket socket)
		{
			lock (_connectionGate)
			{
				if (!ReferenceEquals(_client, socket)) throw new ObjectDisposedException(nameof(Socket));

				LocalEndPoint = socket.LocalEndPoint;
				RemoteEndPoint = socket.RemoteEndPoint;
				IsConnect = true;
			}

			StartReceive(socket);
		}

		public void Close() => Close(expectedSocket: null);

		private void Close(Socket expectedSocket)
		{
			Socket socket;
			EndPoint endPoint;
			bool wasConnected;
			lock (_connectionGate)
			{
				if (expectedSocket == null) _connectAttempt = null;
				else if (!ReferenceEquals(_client, expectedSocket)) return;

				socket = _client;
				if (socket == null) return;
				endPoint = RemoteEndPoint;
				wasConnected = IsConnect;
				IsConnect = false;
				Volatile.Write(ref _client, null);
			}

			try
			{
				using (socket)
				{
					if (wasConnected) socket.Shutdown(SocketShutdown.Both);
				}
			}
			catch (Exception exp)
			{
				_error.OnNext(new ErrorData("Close", exp));
			}
			finally
			{
				if (wasConnected) _closed.OnNext(endPoint);
			}
		}

		/// <summary>
		/// Send data
		/// </summary>
		/// <param name="data"></param>
		/// <returns>The number of bytes sent to the Socket.
		/// If an error occured return -1.</returns>
		public int Send(byte[] data)
		{
			try
			{
				return Client.Send(data);
			}
			catch (Exception exp)
			{
				_error.OnNext(new ErrorData("Send", exp));
				return -1;
			}
		}

		/// <summary>
		/// Set KeepAlive with property values
		/// </summary>
		public void SetKeepAlive() => SetKeepAlive(Client);

		private void SetKeepAlive(Socket socket)
		{
			var inputParams = new byte[12];
			BitConverter.GetBytes(1).CopyTo(inputParams, 0);
			BitConverter.GetBytes(KeepAliveTime).CopyTo(inputParams, 4);
			BitConverter.GetBytes(KeepAliveInterval).CopyTo(inputParams, 8);
			socket?.IOControl(IOControlCode.KeepAliveValues, inputParams, null);
		}

		internal void StartReceive(Socket socket)
		{
			ReceiveState state;
			lock (_connectionGate)
			{
				if (!ReferenceEquals(_client, socket) || !IsConnect) return;

				state = new ReceiveState(socket, LocalEndPoint, RemoteEndPoint);
			}

			StartReceive(state);
		}

		private void StartReceive(ReceiveState state)
		{
			while (ReferenceEquals(Client, state.Socket))
			{
				int bufferSize = BufferSize;
				var buffer = state.Buffer;
				if (buffer == null || buffer.Length != bufferSize)
				{
					buffer = new byte[bufferSize];
					state.Buffer = buffer;
				}

				try
				{
					var result = state.Socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, state);
					if (!result.CompletedSynchronously || !CompleteReceive(state, result)) return;
				}
				catch (Exception exp)
				{
					ReceiveFailed(state.Socket, "BeginReceive", exp);
					return;
				}
			}
		}

		private void ReceiveCallback(IAsyncResult result)
		{
			// StartReceive handles inline completions in its loop.
			if (result.CompletedSynchronously) return;

			var state = (ReceiveState)result.AsyncState;
			if (CompleteReceive(state, result)) StartReceive(state);
		}

		private bool CompleteReceive(ReceiveState state, IAsyncResult result)
		{
			int length;
			try
			{
				length = state.Socket.EndReceive(result);
			}
			catch (Exception exp)
			{
				ReceiveFailed(state.Socket, "EndReceive", exp);
				return false;
			}

			if (!ReferenceEquals(Client, state.Socket)) return false;

			if (length == 0)
			{
				Close(state.Socket);
				return false;
			}

			var data = new byte[length];
			Array.Copy(state.Buffer, data, length);
			_received.OnNext(new TcpData(this, data, state.LocalEndPoint, state.RemoteEndPoint));
			return true;
		}

		private void ReceiveFailed(Socket socket, string method, Exception exception)
		{
			if (!ReferenceEquals(Client, socket)) return;

			try
			{
				_error.OnNext(new ErrorData(method, exception));
			}
			finally
			{
				Close(socket);
			}
		}

		private sealed class ReceiveState
		{
			public ReceiveState(Socket socket, EndPoint localEndPoint, EndPoint remoteEndPoint)
			{
				Socket = socket;
				LocalEndPoint = localEndPoint;
				RemoteEndPoint = remoteEndPoint;
			}

			public Socket Socket { get; }
			public EndPoint LocalEndPoint { get; }
			public EndPoint RemoteEndPoint { get; }
			public byte[] Buffer { get; set; }
		}
	}
}
