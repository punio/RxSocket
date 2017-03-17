using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
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

			_client.Error.ObserveOnDispatcher().Subscribe(e => Log.Add($"Error.{e.Method} - {e.Exception?.Message}"));
			_client.Closed.ObserveOnDispatcher().Subscribe(e => Log.Add($"Client closed.{e.Client?.RemoteEndPoint}"));
			_client.Received.ObserveOnDispatcher().Subscribe(e => Log.Add($"Receive (from {e.From.Client.RemoteEndPoint}).\n{e.Data.ToDumpString()}"));

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
