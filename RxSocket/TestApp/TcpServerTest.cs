using System;
using System.Net;
using Reactive.Bindings;
using RxSocket;

namespace TestApp
{
	public class TcpServerTest
	{
		public ReactiveProperty<string> Address { get; } = new ReactiveProperty<string>(IPAddress.Any.ToString());
		public ReactiveProperty<int> Port { get; } = new ReactiveProperty<int>(10000);

		public ReactiveCommand Listen { get; } = new ReactiveCommand();
		public ReactiveCommand Close { get; } = new ReactiveCommand();
		public ReactiveCommand<string> Broadcast { get; } = new ReactiveCommand<string>();

		public ReactiveCollection<string> Log { get; } = new ReactiveCollection<string>();

		private RxTcpServer _server;

		public TcpServerTest()
		{
			_server = new RxTcpServer();

			_server.Error.Subscribe(e => Log.AddOnScheduler($"Error.{e.Method} - {e.Exception?.Message}"));
			_server.Accepted.Subscribe(e => Log.AddOnScheduler($"Accept. from {e.Client.RemoteEndPoint}"));
			_server.Closed.Subscribe(e => Log.AddOnScheduler($"Closed.{e}"));
			_server.Received.Subscribe(e => Log.AddOnScheduler($"Receive (from {e.From.Client.RemoteEndPoint}).\n{e.Data.ToDumpString()}"));

			Listen.Subscribe(_ =>
			{
				try
				{
					_server.Listen(Address.Value, Port.Value);
					Log.Add($"Listen {Address.Value} : {Port.Value}");
				}
				catch (Exception exp)
				{
					Log.Add($"Listen error.{exp.Message}");
				}
			});

			Close.Subscribe(_ => _server.Close());

			this.Broadcast.Subscribe(data => _server.Broadcast(data.ToSendData()));
		}
	}
}
