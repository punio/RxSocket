using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace RxSocket.Test;

public class TcpServerTests
{
    [Fact]
    public async Task ExchangesDataAndRemovesDisconnectedClient()
    {
        using var timeout = Loopback.CreateTimeout();
        using var server = new RxTcpServer { EnableKeepAlive = false };
        using var client = new RxTcpClient { EnableKeepAlive = false };
        server.Listen(IPAddress.Loopback.ToString(), 0);
        var endpoint = Assert.IsType<IPEndPoint>(server.Server.LocalEndPoint);
        var accepted = server.Accepted.FirstAsync().ToTask(timeout.Token);
        var serverPacket = server.Received.FirstAsync().ToTask(timeout.Token);
        var clientPacket = client.Received.FirstAsync().ToTask(timeout.Token);
        var closed = server.Closed.FirstAsync().ToTask(timeout.Token);

        await client.ConnectAsync(endpoint.Address.ToString(), endpoint.Port).WaitAsync(timeout.Token);
        Assert.Same(await accepted, Assert.Single(server.Clients));

        Assert.Equal(1, client.Send(new byte[] { 42 }));
        var request = await serverPacket;
        Assert.Equal(new byte[] { 42 }, request.Data);
        Assert.Equal(client.LocalEndPoint, request.RemoteEndPoint);

        server.Broadcast(new byte[] { 17 });
        Assert.Equal(new byte[] { 17 }, (await clientPacket).Data);

        var clientEndpoint = client.LocalEndPoint;
        client.Close();
        Assert.Equal(clientEndpoint, await closed);
        Assert.Empty(server.Clients);
    }

    [Fact]
    public async Task CloseInsideAcceptedRemovesClientAndNotifiesServer()
    {
        using var timeout = Loopback.CreateTimeout();
        using var server = new RxTcpServer { EnableKeepAlive = false };
        server.Listen(IPAddress.Loopback.ToString(), 0);
        var endpoint = Assert.IsType<IPEndPoint>(server.Server.LocalEndPoint);
        var result = new TaskCompletionSource<(bool WasRegistered, bool IsConnected)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = server.Closed.FirstAsync().ToTask(timeout.Token);
        using var accepted = server.Accepted.Subscribe(client =>
        {
            bool wasRegistered = server.Clients.Contains(client);
            client.Close();
            result.TrySetResult((wasRegistered, client.IsConnect));
        });

        using var peer = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        var state = await result.Task.WaitAsync(timeout.Token);
        Assert.True(state.WasRegistered);
        Assert.False(state.IsConnected);
        Assert.Equal(peer.LocalEndPoint, await closed);
        Assert.Empty(server.Clients);
    }

    [Fact]
    public async Task ReceiveNotificationsFromTwoClientsAreSerialized()
    {
        using var timeout = Loopback.CreateTimeout();
        using var server = new RxTcpServer { EnableKeepAlive = false };
        using var releaseFirst = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscriber = server.Received.Subscribe(packet =>
        {
            if (packet.Data[0] == 1)
            {
                firstEntered.TrySetResult();
                releaseFirst.Wait(Loopback.Timeout);
            }
            else
            {
                secondEntered.TrySetResult();
            }
        });
        var deliveries = server.Received.Take(2).ToArray().ToTask(timeout.Token);

        server.Listen(IPAddress.Loopback.ToString(), 0);
        var endpoint = Assert.IsType<IPEndPoint>(server.Server.LocalEndPoint);
        var accepted = server.Accepted.Take(2).ToArray().ToTask(timeout.Token);
        using var first = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        using var second = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        await accepted;

        try
        {
            Assert.Equal(1, await first.SendAsync(new byte[] { 1 }, SocketFlags.None, timeout.Token));
            await firstEntered.Task.WaitAsync(timeout.Token);
            Assert.Equal(1, await second.SendAsync(new byte[] { 2 }, SocketFlags.None, timeout.Token));

            // Keep the first callback open while the second peer has data ready.
            var completed = await Task.WhenAny(secondEntered.Task, Task.Delay(500, timeout.Token));
            Assert.NotSame(secondEntered.Task, completed);
        }
        finally
        {
            releaseFirst.Set();
        }

        await secondEntered.Task.WaitAsync(timeout.Token);
        Assert.Equal(new byte[] { 1, 2 }, (await deliveries).SelectMany(packet => packet.Data).ToArray());
    }
}
