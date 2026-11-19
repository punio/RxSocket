using System;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
			if (Client != null) Close();

			if (!IPAddress.TryParse(address, out var targetAddress))
			{
				var hostEntry = Dns.GetHostEntry(address);
				if ((hostEntry.AddressList?.Length ?? 0) <= 0) throw new Exception("Invalid Address");
				targetAddress = hostEntry.AddressList[0];
			}

			var endPoint = new IPEndPoint(targetAddress, port);
			var socket = CreateConnectingSocket(targetAddress.AddressFamily);
			try
			{
				if (EnableKeepAlive) SetKeepAlive(socket);
				socket.Connect(endPoint);
				FinishConnect(socket);
			}
			catch
			{
				Close(socket);
				throw;
			}
		}

		public async Task ConnectAsync(string address, int port)
		{
			if (Client != null) Close();

			if (!IPAddress.TryParse(address, out var targetAddress))
			{
				var hostEntry = await Dns.GetHostEntryAsync(address);
				if ((hostEntry.AddressList?.Length ?? 0) <= 0) throw new Exception("Invalid Address");
				targetAddress = hostEntry.AddressList[0];
			}

			var endPoint = new IPEndPoint(targetAddress, port);
			var socket = CreateConnectingSocket(targetAddress.AddressFamily);
			try
			{
				if (EnableKeepAlive) SetKeepAlive(socket);
				await socket.ConnectAsync(endPoint);
				FinishConnect(socket);
			}
			catch
			{
				Close(socket);
				throw;
			}
		}

		private Socket CreateConnectingSocket(AddressFamily addressFamily)
		{
			lock (_connectionGate)
			{
				if (_client != null) throw new InvalidOperationException("Another socket is already active.");

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

		public void Close() => Close(Client);

		private void Close(Socket socket)
		{
			if (socket == null) return;

			EndPoint endPoint;
			bool wasConnected;
			lock (_connectionGate)
			{
				if (!ReferenceEquals(_client, socket)) return;

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
			if (!ReferenceEquals(Client, state.Socket)) return;

			var buffer = new byte[BufferSize];
			state.Buffer = buffer;
			try
			{
				state.Socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallback, state);
			}
			catch (Exception exp)
			{
				ReceiveFailed(state.Socket, "BeginReceive", exp);
			}
		}

		private void ReceiveCallback(IAsyncResult result)
		{
			var state = (ReceiveState)result.AsyncState;
			int length;
			try
			{
				length = state.Socket.EndReceive(result);
			}
			catch (Exception exp)
			{
				ReceiveFailed(state.Socket, "EndReceive", exp);
				return;
			}

			if (!ReferenceEquals(Client, state.Socket)) return;

			if (length == 0)
			{
				Close(state.Socket);
				return;
			}

			var data = new byte[length];
			Array.Copy(state.Buffer, data, length);
			_received.OnNext(new TcpData(this, data, state.LocalEndPoint, state.RemoteEndPoint));

			StartReceive(state);
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
