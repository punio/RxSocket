using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RxSocket;

namespace TestApp
{
	internal static class TcpPerformance
	{
		private const int BytesPerMiB = 1024 * 1024;
		private const int SendChunkSize = 64 * 1024;
		private const string Usage = "Usage: TestApp --perf [payload-MiB-per-client] [runs-per-buffer] [clients] [--duplex]";

		internal static async Task<int> RunAsync(string[] args)
		{
			if (args is ["--help"])
			{
				Console.WriteLine(Usage);
				Console.WriteLine("Defaults to 64 MiB per client, 3 runs per buffer (1, 4 and 16 KiB), and 1 client.");
				Console.WriteLine("Add --duplex to send the same amount in each direction simultaneously.");
				return 0;
			}

			int payloadMiB = 64;
			int runs = 3;
			int clientCount = 1;
			bool duplex = args.Length > 0 && args[^1] == "--duplex";
			if (duplex) args = args[..^1];

			if (args.Length > 3 ||
				(args.Length > 0 && (!int.TryParse(args[0], out payloadMiB) || payloadMiB <= 0)) ||
				(args.Length > 1 && (!int.TryParse(args[1], out runs) || runs <= 0)) ||
				(args.Length > 2 && (!int.TryParse(args[2], out clientCount) || clientCount <= 0)))
			{
				Console.Error.WriteLine(Usage);
				Console.Error.WriteLine("Payload, run count, and client count must be positive integers.");
				return 2;
			}

			int[] bufferSizes = { 1024, 4 * 1024, 16 * 1024 };
			var sendBuffer = new byte[SendChunkSize];
			long payloadBytes = (long)payloadMiB * BytesPerMiB;
			long warmupBytes = Math.Min(payloadBytes, 4L * BytesPerMiB);
			double transferredMiB = (double)payloadMiB * clientCount * (duplex ? 2 : 1);
			string mode = duplex ? "duplex" : "receive";
			Console.WriteLine($"TCP loopback {mode}, {clientCount} client(s), {payloadMiB} MiB per client per direction, {runs} run(s) per buffer");

			try
			{
				foreach (int bufferSize in bufferSizes)
					await MeasureAsync(bufferSize, warmupBytes, sendBuffer, clientCount, duplex);

				Console.WriteLine($"{"Buffer (KiB)",12} {"Run",5} {"Total MiB/s",12} {"Alloc B/MiB",16} {"Server receives",15}");
				for (int run = 0; run < runs; run++)
				{
					foreach (int bufferSize in bufferSizes)
					{
						var measurement = await MeasureAsync(bufferSize, payloadBytes, sendBuffer, clientCount, duplex);
						double throughput = transferredMiB / measurement.Elapsed.TotalSeconds;
						double allocatedPerMiB = measurement.AllocatedBytes / transferredMiB;
						Console.WriteLine($"{bufferSize / 1024,12} {run + 1,5} {throughput,12:N1} {allocatedPerMiB,16:N0} {measurement.Receives,15:N0}");
					}
				}

				return 0;
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine($"TCP measurement failed: {exception.Message}");
				return 1;
			}
		}

		private static async Task<Measurement> MeasureAsync(int bufferSize, long payloadBytes, byte[] sendBuffer,
			int clientCount, bool duplex)
		{
			long totalUploadBytes = checked(payloadBytes * clientCount);
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
			using var server = new RxTcpServer { BufferSize = bufferSize, EnableKeepAlive = false };
			var peers = new Socket[clientCount];
			var serverSockets = new Socket[clientCount];
			var receivedPerClient = new Dictionary<EndPoint, long>(clientCount);
			var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var completed = new TaskCompletionSource<TransferEnd>(TaskCreationOptions.RunContinuationsAsynchronously);
			var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
			int acceptedCount = 0;
			long receivedBytes = 0;
			long receives = 0;

			using var accepted = server.Accepted.Subscribe(client =>
			{
				serverSockets[acceptedCount++] = client.Client;
				if (acceptedCount == clientCount) ready.TrySetResult();
			});
			using var error = server.Error.Subscribe(failure => failed.TrySetResult(
				new InvalidOperationException($"RxTcpServer reported an error in {failure.Method}.", failure.Exception)));
			using var closed = server.Closed.Subscribe(_ =>
			{
				if (!completed.Task.IsCompleted)
					failed.TrySetResult(new InvalidOperationException("A connection closed before the transfer completed."));
			});
			using var received = server.Received.Subscribe(packet =>
			{
				if (!receivedPerClient.TryGetValue(packet.RemoteEndPoint, out long clientBytes) ||
					clientBytes + packet.Data.Length > payloadBytes)
				{
					failed.TrySetResult(new InvalidOperationException("The server received unexpected data for a client."));
					return;
				}

				receivedPerClient[packet.RemoteEndPoint] = clientBytes + packet.Data.Length;
				receivedBytes += packet.Data.Length;
				receives++;
				if (receivedBytes == totalUploadBytes)
				{
					long endTimestamp = Stopwatch.GetTimestamp();
					long endAllocated = GC.GetTotalAllocatedBytes(precise: true);
					completed.TrySetResult(new TransferEnd(endTimestamp, endAllocated, receives));
				}
			});

			try
			{
				server.Listen(IPAddress.Loopback.ToString(), 0);
				var endpoint = server.Server.LocalEndPoint;
				var connections = new Task[clientCount];
				for (int index = 0; index < clientCount; index++)
				{
					var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
					peers[index] = peer;
					connections[index] = peer.ConnectAsync(endpoint, timeout.Token).AsTask();
				}

				await AwaitOrThrowAsync(Task.WhenAll(connections), failed.Task, timeout.Token);
				await AwaitOrThrowAsync(ready.Task, failed.Task, timeout.Token);
				foreach (var peer in peers)
					receivedPerClient.Add(peer.LocalEndPoint, 0);

				var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var transfers = new List<Task>();
				for (int index = 0; index < clientCount; index++)
				{
					transfers.Add(SendAsync(peers[index], sendBuffer, payloadBytes, start.Task, timeout.Token));
					if (duplex)
					{
						transfers.Add(SendAsync(serverSockets[index], sendBuffer, payloadBytes, start.Task, timeout.Token));
						transfers.Add(ReceiveAsync(peers[index], new byte[SendChunkSize], payloadBytes, start.Task, timeout.Token));
					}
				}
				var transfer = Task.WhenAll(transfers);

				long startAllocated = GC.GetTotalAllocatedBytes(precise: true);
				long startTimestamp = Stopwatch.GetTimestamp();
				start.SetResult();
				await AwaitOrThrowAsync(transfer, failed.Task, timeout.Token);
				await AwaitOrThrowAsync(completed.Task, failed.Task, timeout.Token);
				var end = await completed.Task;
				long endTimestamp = duplex ? Stopwatch.GetTimestamp() : end.Timestamp;
				long endAllocated = duplex ? GC.GetTotalAllocatedBytes(precise: true) : end.AllocatedBytes;
				return new Measurement(Stopwatch.GetElapsedTime(startTimestamp, endTimestamp),
					endAllocated - startAllocated, end.Receives);
			}
			catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
			{
				throw new TimeoutException("The TCP transfer did not finish within 60 seconds.", exception);
			}
			finally
			{
				foreach (var peer in peers) peer?.Dispose();
			}
		}

		private static async Task AwaitOrThrowAsync(Task operation, Task<Exception> failed, CancellationToken cancellationToken)
		{
			if (await Task.WhenAny(operation, failed).WaitAsync(cancellationToken) == failed)
				throw await failed;

			await operation;
		}

		private static async Task SendAsync(Socket socket, byte[] buffer, long bytes, Task start, CancellationToken cancellationToken)
		{
			await start;
			while (bytes > 0)
			{
				int length = (int)Math.Min(buffer.Length, bytes);
				int sent = await socket.SendAsync(buffer.AsMemory(0, length), SocketFlags.None, cancellationToken);
				if (sent == 0) throw new InvalidOperationException("A sender closed before finishing its transfer.");
				bytes -= sent;
			}
		}

		private static async Task ReceiveAsync(Socket socket, byte[] buffer, long bytes, Task start, CancellationToken cancellationToken)
		{
			await start;
			while (bytes > 0)
			{
				int length = (int)Math.Min(buffer.Length, bytes);
				int received = await socket.ReceiveAsync(buffer.AsMemory(0, length), SocketFlags.None, cancellationToken);
				if (received == 0) throw new InvalidOperationException("A client did not receive its full downlink payload.");
				bytes -= received;
			}
		}

		private readonly record struct TransferEnd(long Timestamp, long AllocatedBytes, long Receives);

		private readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes, long Receives);
	}
}
