using System;
using Reactive.Bindings;
using RxSocket;

namespace TestApp
{
	public class TcpClientTest
	{
		public ReactiveProperty<string> Address { get; } = new ReactiveProperty<string>("127.0.0.1");
		public ReactiveProperty<int> Port { get; } = new ReactiveProperty<int>(10000);

		public ReactiveCommand Connect { get; } = new ReactiveCommand();
		public ReactiveCommand Close { get; } = new ReactiveCommand();
		public ReactiveCommand<string> Send { get; } = new ReactiveCommand<string>();

		public ReactiveCollection<string> Log { get; } = new ReactiveCollection<string>();

		private RxTcpClient _client;

		public TcpClientTest()
		{
			_client = new RxTcpClient();

			_client.Error.Subscribe(e => Log.AddOnScheduler($"Error.{e.Method} - {e.Exception?.Message}"));
			_client.Closed.Subscribe(e => Log.AddOnScheduler($"Client closed.{e}"));
			_client.Received.Subscribe(e => Log.AddOnScheduler($"Receive (from {e.From.Client.RemoteEndPoint}).\n{e.Data.ToDumpString()}"));

			Connect.Subscribe(_ =>
			{
				try
				{
					_client.Connect(Address.Value, Port.Value);
					Log.Add($"Connect {_client.Client.LocalEndPoint} -> {_client.Client.RemoteEndPoint}");
				}
				catch (Exception exp)
				{
					Log.Add($"Connect error.{exp.Message}");
				}
			});

			Close.Subscribe(_ => _client.Close());

			Send.Subscribe(data => _client.Send(data.ToSendData()));
		}
	}
}
