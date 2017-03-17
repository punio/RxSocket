using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp
{
	public class MainViewModel
	{
		public TcpServerTest Server { get; } = new TcpServerTest();
		public TcpClientTest Client { get; } = new TcpClientTest();
	}
}
