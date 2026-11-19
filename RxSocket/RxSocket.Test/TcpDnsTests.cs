using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace RxSocket.Test;

public class TcpDnsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientTriesNextDnsAddressAfterConnectionRefusal(bool asynchronous)
    {
        using var timeout = Loopback.CreateTimeout();
        var (first, second) = GetLocalhostAddresses();
        using var listener = Loopback.BindTcpSocket(second);
        listener.Listen(1);
        int port = Assert.IsType<IPEndPoint>(listener.LocalEndPoint).Port;
        using var unavailable = Loopback.BindTcpSocket(first, port);
        using var client = new RxTcpClient { EnableKeepAlive = false };

        if (asynchronous)
            await client.ConnectAsync("localhost", port).WaitAsync(timeout.Token);
        else
            client.Connect("localhost", port);

        using var accepted = await listener.AcceptAsync(timeout.Token);
        Assert.True(client.IsConnect);
        Assert.Equal(listener.LocalEndPoint, client.RemoteEndPoint);
        Assert.Equal(client.LocalEndPoint, accepted.RemoteEndPoint);
    }

    [Fact]
    public async Task ServerTriesNextDnsAddressWhenFirstPortIsOccupied()
    {
        using var timeout = Loopback.CreateTimeout();
        var (first, second) = GetLocalhostAddresses();
        using var occupied = Loopback.BindTcpSocket(first);
        occupied.Listen(1);
        int port = Assert.IsType<IPEndPoint>(occupied.LocalEndPoint).Port;
        using var server = new RxTcpServer { EnableKeepAlive = false };
        var received = server.Received.FirstAsync().ToTask(timeout.Token);

        server.Listen("localhost", port);
        var endpoint = new IPEndPoint(second, port);
        Assert.Equal(endpoint, server.Server.LocalEndPoint);

        using var client = await Loopback.ConnectTcpPeerAsync(endpoint, timeout.Token);
        Assert.Equal(1, await client.SendAsync(new byte[] { 42 }, SocketFlags.None, timeout.Token));
        Assert.Equal(new byte[] { 42 }, (await received).Data);
    }

    [Fact]
    public void ServerCanRetryAfterAllDnsAddressesFail()
    {
        var (first, second) = GetLocalhostAddresses();
        using var server = new RxTcpServer { EnableKeepAlive = false };
        int port;
        using (var firstListener = Loopback.BindTcpSocket(first))
        {
            firstListener.Listen(1);
            port = Assert.IsType<IPEndPoint>(firstListener.LocalEndPoint).Port;
            using var secondListener = Loopback.BindTcpSocket(second, port);
            secondListener.Listen(1);

            Assert.Throws<SocketException>(() => server.Listen("localhost", port));
            Assert.Null(server.Server);
            Assert.Empty(server.Clients);
        }

        server.Listen("localhost", port);
        var listeningSocket = Assert.IsType<Socket>(server.Server);
        Assert.Equal(new IPEndPoint(first, port), listeningSocket.LocalEndPoint);
    }

    private static (IPAddress First, IPAddress Second) GetLocalhostAddresses()
    {
        Assert.SkipUnless(Socket.OSSupportsIPv4 && Socket.OSSupportsIPv6, "Dual-stack loopback is required.");
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses("localhost").Distinct().ToArray();
        }
        catch (SocketException exception)
        {
            Assert.Skip($"localhost could not be resolved ({exception.SocketErrorCode}).");
            throw;
        }

        Assert.SkipUnless(
            addresses.Length == 2 && addresses.All(IPAddress.IsLoopback) &&
            addresses[0].AddressFamily != addresses[1].AddressFamily,
            "localhost must resolve to one IPv4 and one IPv6 loopback address.");
        try
        {
            var ipv6 = addresses.Single(address => address.AddressFamily == AddressFamily.InterNetworkV6);
            using var probe = Loopback.BindTcpSocket(ipv6);
        }
        catch (SocketException exception) when (
            exception.SocketErrorCode is SocketError.AddressFamilyNotSupported or SocketError.AddressNotAvailable)
        {
            Assert.Skip($"IPv6 loopback could not be bound ({exception.SocketErrorCode}).");
        }

        return (addresses[0], addresses[1]);
    }
}
