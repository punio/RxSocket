using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RxSocket
{
	public class TcpData
	{
		[Obsolete]
		public RxTcpClient From { get; }
		public byte[] Data { get; }

		public EndPoint LocalEndPoint { get; }
		public EndPoint RemoteEndPoint { get; }

		[Obsolete]
		public TcpData(RxTcpClient client, byte[] data)
		{
			From = client;
			Data = data;
		}

		public TcpData(RxTcpClient client, byte[] data, EndPoint localEndPoint, EndPoint remoteEndPoint)
		{
			From = client;
			Data = data;
			LocalEndPoint = localEndPoint;
			RemoteEndPoint = remoteEndPoint;
		}
	}
}
