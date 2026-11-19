using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace RxSocket.Test;

public class TcpServerTests
{
    [Fact]
    public async Task ClientsReturnsAReadOnlySnapshotOfMembership()
    {
        using var timeout = Loopback.CreateTimeout();
        using var server = new RxTcpServer { EnableKeepAlive = false };
        server.Listen(IPAddress.Loopback.ToString(), 0);
        var endpoint = Assert.IsType<IPEndPoint>(server.Server.LocalEndPoint);
        var accepted = server.Accepted.FirstAsync().ToTask(timeout.Token);
        using var firstPeer = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        var firstClient = await accepted;
        var snapshot = server.Clients;
        Assert.Same(firstClient, Assert.Single(snapshot));
        Assert.Throws<NotSupportedException>(() => ((ICollection<RxTcpClient>)snapshot).Clear());

        accepted = server.Accepted.FirstAsync().ToTask(timeout.Token);
        using var secondPeer = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        var secondClient = await accepted;
        Assert.Contains(secondClient, server.Clients);
        Assert.Same(firstClient, Assert.Single(snapshot));

        var closed = server.Closed.FirstAsync().ToTask(timeout.Token);
        firstPeer.Shutdown(SocketShutdown.Send);
        Assert.Equal(firstPeer.LocalEndPoint, await closed);
        Assert.Same(secondClient, Assert.Single(server.Clients));
        Assert.Same(firstClient, Assert.Single(snapshot));
    }

    [Fact]
    public async Task BroadcastSendsToEveryConnectedClient()
    {
        using var timeout = Loopback.CreateTimeout();
        using var server = new RxTcpServer { EnableKeepAlive = false };
        server.Listen(IPAddress.Loopback.ToString(), 0);
        var endpoint = Assert.IsType<IPEndPoint>(server.Server.LocalEndPoint);
        var accepted = server.Accepted.Take(2).ToArray().ToTask(timeout.Token);
        using var first = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        using var second = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        await accepted;

        var firstData = new byte[1];
        var secondData = new byte[1];
        var firstReceive = first.ReceiveAsync(firstData, SocketFlags.None, timeout.Token).AsTask();
        var secondReceive = second.ReceiveAsync(secondData, SocketFlags.None, timeout.Token).AsTask();

        server.Broadcast(new byte[] { 42 });

        Assert.Equal(1, await firstReceive);
        Assert.Equal(1, await secondReceive);
        Assert.Equal(new byte[] { 42 }, firstData);
        Assert.Equal(new byte[] { 42 }, secondData);
    }

    [Fact]
    public async Task ReceivesLargeTransferWithSmallBuffer()
    {
        const int payloadBytes = 4 * 1024 * 1024;
        var sendBuffer = new byte[64 * 1024];
        using var timeout = Loopback.CreateTimeout();
        using var server = new RxTcpServer { EnableKeepAlive = false, BufferSize = 1024 };
        var received = server.Received
            .Scan(0, (total, packet) => total + packet.Data.Length)
            .FirstAsync(total => total >= payloadBytes)
            .ToTask(timeout.Token);
        server.Listen(IPAddress.Loopback.ToString(), 0);
        var endpoint = Assert.IsType<IPEndPoint>(server.Server.LocalEndPoint);
        using var peer = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);

        int remaining = payloadBytes;
        while (remaining > 0)
        {
            int length = Math.Min(remaining, sendBuffer.Length);
            int sent = await peer.SendAsync(sendBuffer.AsMemory(0, length), SocketFlags.None, timeout.Token);
            Assert.True(sent > 0);
            remaining -= sent;
        }

        Assert.Equal(payloadBytes, await received);
    }

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
