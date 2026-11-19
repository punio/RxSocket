using System;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RxSocket;

namespace TestApp
{
	internal static class UdpPerformance
	{
		private const int BytesPerMiB = 1024 * 1024;
		private const int MaxPayloadBytes = 65_507;
		private const string Usage = "Usage: TestApp --perf-udp [datagrams-per-sender] [payload-bytes] [runs] [senders]";
		private static readonly TimeSpan DrainTime = TimeSpan.FromMilliseconds(250);

		internal static async Task<int> RunAsync(string[] args)
		{
			if (args is ["--help"])
			{
				Console.WriteLine(Usage);
				Console.WriteLine("Defaults to 50,000 datagrams of 1,200 bytes, 3 runs, and 1 sender.");
				return 0;
			}

			int datagrams = 50_000;
			int payloadBytes = 1200;
			int runs = 3;
			int senderCount = 1;
			if (args.Length > 4 ||
				(args.Length > 0 && (!int.TryParse(args[0], out datagrams) || datagrams <= 0)) ||
				(args.Length > 1 && (!int.TryParse(args[1], out payloadBytes) || payloadBytes < sizeof(int) || payloadBytes > MaxPayloadBytes)) ||
				(args.Length > 2 && (!int.TryParse(args[2], out runs) || runs <= 0)) ||
				(args.Length > 3 && (!int.TryParse(args[3], out senderCount) || senderCount <= 0)) ||
				(long)datagrams * senderCount > int.MaxValue)
			{
				Console.Error.WriteLine(Usage);
				Console.Error.WriteLine($"Use positive counts, a payload of {sizeof(int)} to {MaxPayloadBytes} bytes, " +
					$"and at most {int.MaxValue} datagrams in total per run.");
				return 2;
			}

			int sent = datagrams * senderCount;
			Console.WriteLine($"UDP loopback receive, {senderCount:N0} sender(s), {datagrams:N0} datagrams per sender, {payloadBytes:N0} bytes per datagram");

			try
			{
				await MeasureAsync(Math.Min(datagrams, 1000), payloadBytes, senderCount);

				Console.WriteLine($"{"Run",4} {"Sent",12} {"Received",12} {"Loss %",9} {"Datagrams/s",14} {"MiB/s",10} {"Alloc B/MiB",16}");
				for (int run = 0; run < runs; run++)
				{
					var measurement = await MeasureAsync(datagrams, payloadBytes, senderCount);
					double seconds = measurement.Elapsed.TotalSeconds;
					double receivedMiB = (long)measurement.Received * payloadBytes / (double)BytesPerMiB;
					double lossPercent = 100.0 * (sent - measurement.Received) / sent;
					string allocatedPerMiB = receivedMiB > 0
						? (measurement.AllocatedBytes / receivedMiB).ToString("N0")
						: "-";
					Console.WriteLine($"{run + 1,4} {sent,12:N0} {measurement.Received,12:N0} {lossPercent,9:N2} " +
						$"{measurement.Received / seconds,14:N0} {receivedMiB / seconds,10:N1} {allocatedPerMiB,16}");
				}

				return 0;
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine($"UDP measurement failed: {exception.Message}");
				return 1;
			}
		}

		private static async Task<Measurement> MeasureAsync(int datagrams, int payloadBytes, int senderCount)
		{
			int sent = datagrams * senderCount;
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
			using var listener = new RxUdpListener { LocalAddress = IPAddress.Loopback };
			var senders = new Socket[senderCount];
			var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
			var seen = new BitArray(sent);
			int receivedCount = 0;
			long lastReceiveTimestamp = 0;

			using var error = listener.Error.Subscribe(failure => failed.TrySetResult(
				new InvalidOperationException($"RxUdpListener reported an error in {failure.Method}.", failure.Exception)));
			using var received = listener.Received.Subscribe(packet =>
			{
				if (packet.Data.Length != payloadBytes) return;
				int sequence = BinaryPrimitives.ReadInt32LittleEndian(packet.Data);
				if (sequence < 0 || sequence >= sent) return;

				lock (seen)
				{
					if (seen[sequence]) return;
					seen[sequence] = true;
					lastReceiveTimestamp = Stopwatch.GetTimestamp();
					if (++receivedCount == sent) allReceived.TrySetResult();
				}
			});

			try
			{
				listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
				var destination = listener.Client.Client.LocalEndPoint;
				var sends = new Task[senderCount];
				for (int index = 0; index < senders.Length; index++)
				{
					var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
					senders[index] = sender;
					sender.Connect(destination);
					sends[index] = SendAsync(sender, new byte[payloadBytes], index * datagrams, datagrams, start.Task, timeout.Token);
				}
				var sending = Task.WhenAll(sends);

				long startAllocated = GC.GetTotalAllocatedBytes(precise: true);
				long startTimestamp = Stopwatch.GetTimestamp();
				start.SetResult();
				if (await Task.WhenAny(sending, failed.Task).WaitAsync(timeout.Token) == failed.Task)
					throw await failed.Task;

				await sending;
				long sendEnd = Stopwatch.GetTimestamp();
				await Task.WhenAny(allReceived.Task, failed.Task, Task.Delay(DrainTime, timeout.Token));
				if (failed.Task.IsCompletedSuccessfully) throw await failed.Task;

				timeout.Token.ThrowIfCancellationRequested();
				lock (seen)
				{
					long endTimestamp = Math.Max(sendEnd, lastReceiveTimestamp);
					long allocated = GC.GetTotalAllocatedBytes(precise: true) - startAllocated;
					return new Measurement(Stopwatch.GetElapsedTime(startTimestamp, endTimestamp), allocated, receivedCount);
				}
			}
			catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
			{
				throw new TimeoutException("The UDP measurement did not finish within 60 seconds.", exception);
			}
			finally
			{
				start.TrySetCanceled();
				timeout.Cancel();
				foreach (var sender in senders) sender?.Dispose();
			}
		}

		private static async Task SendAsync(Socket sender, byte[] payload, int firstSequence, int count, Task start,
			CancellationToken cancellationToken)
		{
			await start;
			for (int index = 0; index < count; index++)
			{
				BinaryPrimitives.WriteInt32LittleEndian(payload, firstSequence + index);
				int sent = await sender.SendAsync(payload.AsMemory(), SocketFlags.None, cancellationToken);
				if (sent != payload.Length) throw new InvalidOperationException("A UDP datagram was not sent in full.");
			}
		}

		private readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes, int Received);
	}
}
