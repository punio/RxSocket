using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RxSocket
{
	public class UdpData
	{
		public IPEndPoint From { get; }
		public byte[] Data { get; }

		public UdpData(IPEndPoint @from, byte[] data)
		{
			From = @from;
			Data = data;
		}
	}
}
