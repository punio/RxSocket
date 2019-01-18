using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp
{
	static class Functions
	{
		public static string ToDumpString(this byte[] data)
		{
			var total = new StringBuilder();
			for (var i = 0; i < data.Length; i += 16)
			{
				var by16 = new byte[Math.Min(data.Length - i, 16)];
				Array.Copy(data, i, by16, 0, by16.Length);
				var s = new StringBuilder(64);
				s.AppendFormat(" {0:X4}  | ", i);
				var j = 0;
				var ss = new StringBuilder(64);
				for (j = 0; j < by16.Length; j++)
				{
					s.Append(by16[j].ToString("X2") + " ");
					if (by16[j] >= 0x7F)
					{
						ss.Append(".");
					}
					else
					{
						if (by16[j] < 0x20)
						{
							ss.Append(".");
						}
						else
						{
							ss.Append((char)by16[j]);
						}
					}
				}
				for (; j < 16; j++)
				{
					s.Append("   ");
					ss.Append(" ");
				}
				ss.Append("|");
				s.AppendFormat("|{0}", ss);
				total.Append("\r\n");
				total.Append(s);
			}

			return total.ToString();
		}

		public static byte[] ToSendData(this string data)
		{
			var cols = data.Split(' ', ',', '-');
			return cols.Select(hex =>
			{
				byte b = 0;
				byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out b);
				return b;
			}).ToArray();
		}
	}
}
