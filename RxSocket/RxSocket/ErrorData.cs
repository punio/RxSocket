using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RxSocket
{
	public class ErrorData
	{
		public ErrorData(string method,Exception exp)
		{
			Method = method;
			Exception = exp;
		}

		public string Method { get; }
		public Exception Exception { get; }
	}
}
