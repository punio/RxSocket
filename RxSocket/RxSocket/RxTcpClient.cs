using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace RxSocket
{
	public class RxTcpClient : IDisposable
	{
		#region Property
		public Socket Client { get; private set; }
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
		private bool _closing;
		#endregion

		public RxTcpClient()
		{
			Error = _error.AsObservable();
			Closed = _closed.AsObservable();
			Received = _received.AsObservable();
		}

		public RxTcpClient(Socket socket) : this()
		{
			Client = socket;
			IsConnect = true;
			LocalEndPoint = Client.LocalEndPoint;
			RemoteEndPoint = Client.RemoteEndPoint;
			StartReceive();
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
			Client = new Socket(targetAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			if (EnableKeepAlive) SetKeepAlive();

			Client.Connect(endPoint);
			LocalEndPoint = Client.LocalEndPoint;
			RemoteEndPoint = Client.RemoteEndPoint;
			StartReceive();
			IsConnect = true;
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
			Client = new Socket(targetAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			if (EnableKeepAlive) SetKeepAlive();

			await Client.ConnectAsync(endPoint);
			LocalEndPoint = Client.LocalEndPoint;
			RemoteEndPoint = Client.RemoteEndPoint;
			StartReceive();
			IsConnect = true;
		}

		public void Close()
		{
			if (_closing) return;

			if (Client == null)
			{
				IsConnect = false;
				return;
			}

			_closing = true;
			var endPoint = RemoteEndPoint;
			try
			{
				endPoint = Client.RemoteEndPoint;
				if (IsConnect) Client.Shutdown(SocketShutdown.Both);
				Client.Close();
				Client = null;
			}
			catch (Exception exp)
			{
				_error.OnNext(new ErrorData("Close", exp));
			}
			if (IsConnect) _closed.OnNext(endPoint);
			IsConnect = false;
			_closing = false;
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
		public void SetKeepAlive()
		{
			var inputParams = new byte[12];
			BitConverter.GetBytes(1).CopyTo(inputParams, 0);
			BitConverter.GetBytes(KeepAliveTime).CopyTo(inputParams, 4);
			BitConverter.GetBytes(KeepAliveInterval).CopyTo(inputParams, 8);
			Client?.IOControl(IOControlCode.KeepAliveValues, inputParams, null);
		}

		private void StartReceive()
		{
			var buffer = new byte[BufferSize];
			try
			{
				Client.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, RecieveCallback, buffer);
			}
			catch (Exception exp)
			{
				_error.OnNext(new ErrorData("BeginReceive", exp));
			}
		}

		private void RecieveCallback(IAsyncResult result)
		{
			if (_closing || Client == null) return;

			var length = 0;
			try
			{
				length = Client.EndReceive(result);
			}
			catch (Exception exp)
			{
				_error.OnNext(new ErrorData("EndReceive", exp));
			}

			if (length <= 0)    // Socket Closed
			{
				Close();
				return;
			}

			var buffer = (byte[])result.AsyncState;
			var data = new byte[length];
			Array.Copy(buffer, data, data.Length);
			this._received.OnNext(new TcpData(this, data, LocalEndPoint, RemoteEndPoint));

			if (_closing) return;
			StartReceive();
		}
	}
}
