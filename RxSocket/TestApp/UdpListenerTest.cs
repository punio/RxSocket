using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Reactive.Bindings;
using RxSocket;

namespace TestApp
{
	public class UdpListenerTest
	{
		public ReactiveProperty<bool> Multicast { get; } = new ReactiveProperty<bool>();
		public ReactiveProperty<string> Address { get; } = new ReactiveProperty<string>("224.0.0.0");
		public ReactiveProperty<int> Port { get; } = new ReactiveProperty<int>(10000);

		public ReactiveCommand Listen { get; } = new ReactiveCommand();
		public ReactiveCommand Close { get; } = new ReactiveCommand();

		public ReactiveCollection<string> Log { get; } = new ReactiveCollection<string>();

		private RxUdpListener _listener;

		public UdpListenerTest()
		{
			_listener = new RxUdpListener();
			_listener.Received.Subscribe(e => Log.AddOnScheduler($"Receive (from {e.From}).\n{e.Data.ToDumpString()}"));

			Listen.Subscribe(_ =>
			{
				try
				{
					if (Multicast.Value)
					{
						_listener.Listen(Address.Value, Port.Value);
					}
					else
					{
						_listener.Listen(Port.Value);
					}
					Log.Add($"Listen {(Multicast.Value ? Address.Value + " : " : "")}{Port.Value}");
				}
				catch (Exception exp)
				{
					Log.Add($"Listen error.{exp.Message}");
				}
			});

			Close.Subscribe(_ => _listener.Close());
		}
	}
}
