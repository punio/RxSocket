using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RxSocket
{
	public class ReceiveData : EventArgs
	{
		public RxTcpClient From { get; }
		public byte[] Data { get; }

		public ReceiveData(RxTcpClient client, byte[] data)
		{
			From = client;
			Data = data;
		}
	}
}
