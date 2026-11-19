using System.Net;
using System.Net.Sockets;

namespace RxSocket.Test;

public class TcpClientTests
{
    [Fact]
    public async Task ConnectionRefusalReleasesSocketAndAllowsRetry()
    {
        using var timeout = Loopback.CreateTimeout();
        using var boundSocket = Loopback.BindTcpSocket(IPAddress.Loopback);
        var endpoint = Assert.IsType<IPEndPoint>(boundSocket.LocalEndPoint);
        using var client = new RxTcpClient { EnableKeepAlive = false };

        await Assert.ThrowsAsync<SocketException>(() =>
            client.ConnectAsync(endpoint.Address.ToString(), endpoint.Port).WaitAsync(timeout.Token));
        Assert.Null(client.Client);
        Assert.False(client.IsConnect);

        boundSocket.Listen(1);
        await client.ConnectAsync(endpoint.Address.ToString(), endpoint.Port).WaitAsync(timeout.Token);
        using var peer = await boundSocket.AcceptAsync(timeout.Token);
        Assert.True(client.IsConnect);
        Assert.Equal(endpoint, client.RemoteEndPoint);
    }

    [Fact]
    public async Task ReconnectFromReceiveStartsOnlyOneReceiveOnTheNewSocket()
    {
        using var timeout = Loopback.CreateTimeout();
        using var original = Loopback.BindTcpSocket(IPAddress.Loopback);
        using var replacement = Loopback.BindTcpSocket(IPAddress.Loopback);
        original.Listen(1);
        replacement.Listen(1);
        var originalEndpoint = Assert.IsType<IPEndPoint>(original.LocalEndPoint);
        var replacementEndpoint = Assert.IsType<IPEndPoint>(replacement.LocalEndPoint);
        using var client = new RxTcpClient { EnableKeepAlive = false, BufferSize = 1 };
        using var releaseFirst = new ManualResetEventSlim();
        var reconnect = new TaskCompletionSource<EndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEntered = new TaskCompletionSource<TcpData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<TcpData>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var received = client.Received.Subscribe(packet =>
        {
            switch (packet.Data[0])
            {
                case 1:
                    try
                    {
                        client.Connect(replacementEndpoint.Address.ToString(), replacementEndpoint.Port);
                        reconnect.TrySetResult(packet.RemoteEndPoint);
                    }
                    catch (Exception exception)
                    {
                        reconnect.TrySetException(exception);
                    }
                    break;
                case 2:
                    firstEntered.TrySetResult(packet);
                    releaseFirst.Wait(Loopback.Timeout);
                    break;
                case 3:
                    secondEntered.TrySetResult(packet);
                    break;
            }
        });

        client.Connect(originalEndpoint.Address.ToString(), originalEndpoint.Port);
        using var originalPeer = await original.AcceptAsync(timeout.Token);
        Assert.Equal(1, await originalPeer.SendAsync(new byte[] { 1 }, SocketFlags.None, timeout.Token));
        Assert.Equal(originalEndpoint, await reconnect.Task.WaitAsync(timeout.Token));
        using var replacementPeer = await replacement.AcceptAsync(timeout.Token);

        try
        {
            Assert.Equal(1, await replacementPeer.SendAsync(new byte[] { 2 }, SocketFlags.None, timeout.Token));
            Assert.Equal(replacementEndpoint, (await firstEntered.Task.WaitAsync(timeout.Token)).RemoteEndPoint);
            Assert.Equal(1, await replacementPeer.SendAsync(new byte[] { 3 }, SocketFlags.None, timeout.Token));

            // A second receive on the new socket would deliver this byte before the first callback returns.
            var completed = await Task.WhenAny(secondEntered.Task, Task.Delay(500, timeout.Token));
            Assert.NotSame(secondEntered.Task, completed);
        }
        finally
        {
            releaseFirst.Set();
        }

        Assert.Equal(replacementEndpoint, (await secondEntered.Task.WaitAsync(timeout.Token)).RemoteEndPoint);
        Assert.True(client.IsConnect);
    }
}
