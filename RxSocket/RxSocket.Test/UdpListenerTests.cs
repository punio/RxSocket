using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace RxSocket.Test;

public class UdpListenerTests
{
    [Fact]
    public void ReceiveBufferSizeIsAppliedWhenListeningStarts()
    {
        const int requestedSize = 1024 * 1024;
        using var defaultSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using var configuredSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = requestedSize
        };
        using var listener = new RxUdpListener { LocalAddress = IPAddress.Loopback };
        Assert.Throws<ArgumentOutOfRangeException>(() => listener.ReceiveBufferSize = -1);
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
        Assert.Equal(defaultSocket.ReceiveBufferSize, listener.Client.Client.ReceiveBufferSize);

        listener.ReceiveBufferSize = requestedSize;
        Assert.Equal(defaultSocket.ReceiveBufferSize, listener.Client.Client.ReceiveBufferSize);

        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
        Assert.Equal(configuredSocket.ReceiveBufferSize, listener.Client.Client.ReceiveBufferSize);
    }

    [Fact]
    public async Task FastAndObservableReceiversCanReadTheSameDatagrams()
    {
        using var timeout = Loopback.CreateTimeout();
        using var listener = new RxUdpListener { LocalAddress = IPAddress.Loopback };
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var observed = listener.Received.Take(2).ToArray().ToTask(timeout.Token);
        var firstFast = new TaskCompletionSource<UdpData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFast = new TaskCompletionSource<UdpData>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.DatagramReceived += (from, data) =>
        {
            var received = data.Length == 3 ? firstFast : secondFast;
            received.TrySetResult(new UdpData(from, data.ToArray()));
        };
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
        var endpoint = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint);

        byte[] firstPayload = { 1, 2, 3 };
        byte[] secondPayload = { 9, 8 };
        await sender.SendAsync(firstPayload, endpoint, timeout.Token);
        var firstPacket = await firstFast.Task.WaitAsync(timeout.Token);
        await sender.SendAsync(secondPayload, endpoint, timeout.Token);
        var secondPacket = await secondFast.Task.WaitAsync(timeout.Token);
        var packets = await observed;

        Assert.Equal(sender.Client.LocalEndPoint, firstPacket.From);
        Assert.Equal(sender.Client.LocalEndPoint, secondPacket.From);
        Assert.Equal(firstPayload, firstPacket.Data);
        Assert.Equal(secondPayload, secondPacket.Data);
        Assert.Equal(firstPayload, packets[0].Data);
        Assert.Equal(secondPayload, packets[1].Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FastReceiverHandlesEmptyDatagramsWithoutAnObservableSubscription(bool ipv6)
    {
        if (ipv6 && !Socket.OSSupportsIPv6) Assert.Skip("IPv6 is unavailable.");
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var timeout = Loopback.CreateTimeout();
        using var listener = new RxUdpListener { LocalAddress = address };
        using var sender = new UdpClient(new IPEndPoint(address, 0));
        var received = new TaskCompletionSource<(IPEndPoint From, int Length)>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.DatagramReceived += (from, data) => received.TrySetResult((from, data.Length));
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true, ipV6: ipv6);
        var endpoint = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint);

        Assert.Equal(0, await sender.SendAsync(Array.Empty<byte>(), endpoint, timeout.Token));
        var datagram = await received.Task.WaitAsync(timeout.Token);
        Assert.Equal(sender.Client.LocalEndPoint, datagram.From);
        Assert.Equal(0, datagram.Length);
    }

    [Fact]
    public async Task FastHandlerFailureStopsListeningAndReportsError()
    {
        using var timeout = Loopback.CreateTimeout();
        using var listener = new RxUdpListener { LocalAddress = IPAddress.Loopback };
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var failure = listener.Error.FirstAsync().ToTask(timeout.Token);
        var expectedError = new InvalidOperationException("Test handler failure.");
        UdpDatagramHandler handler = (_, _) => throw expectedError;
        listener.DatagramReceived += handler;
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
        var endpoint = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint);

        await sender.SendAsync(new byte[] { 42 }, endpoint, timeout.Token);
        var error = await failure;
        Assert.Equal(nameof(RxUdpListener.DatagramReceived), error.Method);
        Assert.Same(expectedError, error.Exception);
        Assert.Null(listener.Client);

        listener.DatagramReceived -= handler;
        listener.Listen(endpoint.Port, enableBroadcast: false, exclusiveAddressUse: true);
        var reopenedClient = Assert.IsType<UdpClient>(listener.Client);
        Assert.Equal(endpoint, reopenedClient.Client.LocalEndPoint);
    }

    [Fact]
    public async Task ReceivesDatagramWithSenderEndpoint()
    {
        using var timeout = Loopback.CreateTimeout();
        using var listener = new RxUdpListener { LocalAddress = IPAddress.Loopback };
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var received = listener.Received.FirstAsync().ToTask(timeout.Token);
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);

        var endpoint = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint);
        var payload = new byte[] { 1, 2, 3 };
        Assert.Equal(payload.Length, await sender.SendAsync(payload, endpoint, timeout.Token));

        var packet = await received;
        Assert.Equal(sender.Client.LocalEndPoint, packet.From);
        Assert.Equal(payload, packet.Data);
    }

    [Fact]
    public async Task ReceiveFailureReleasesPortAndExistingSubscriptionResumesAfterListen()
    {
        using var timeout = Loopback.CreateTimeout();
        using var listener = new RxUdpListener { LocalAddress = IPAddress.Loopback };
        var failure = listener.Error.FirstAsync().ToTask(timeout.Token);
        var received = listener.Received.FirstAsync().ToTask(timeout.Token);
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
        var endpoint = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint);

        listener.Client.Client.Dispose();

        var error = await failure;
        Assert.Equal(nameof(UdpClient.ReceiveAsync), error.Method);
        Assert.NotNull(error.Exception);
        Assert.Null(listener.Client);
        Assert.Null(listener.MulticastAddress);
        Assert.False(listener.IsMulticast);

        listener.Listen(endpoint.Port, enableBroadcast: false, exclusiveAddressUse: true);
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Assert.Equal(1, await sender.SendAsync(new byte[] { 42 }, endpoint, timeout.Token));
        Assert.Equal(new byte[] { 42 }, (await received).Data);
    }

    [Fact]
    public async Task NormalCloseAndRelistenDoNotReportReceiveErrors()
    {
        using var timeout = Loopback.CreateTimeout();
        using var listener = new RxUdpListener { LocalAddress = IPAddress.Loopback };
        int errors = 0;
        using var subscription = listener.Error.Subscribe(_ => Interlocked.Increment(ref errors));
        var received = listener.Received.FirstAsync().ToTask(timeout.Token);
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
        var endpoint = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint);

        listener.Close();
        listener.Listen(endpoint.Port, enableBroadcast: false, exclusiveAddressUse: true);
        listener.Listen(endpoint.Port, enableBroadcast: false, exclusiveAddressUse: true);
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Assert.Equal(1, await sender.SendAsync(new byte[] { 42 }, endpoint, timeout.Token));

        Assert.Equal(new byte[] { 42 }, (await received).Data);
        Assert.Equal(0, Volatile.Read(ref errors));
    }

    [Fact]
    public void FailedMulticastJoinReleasesPort()
    {
        using var listener = new RxUdpListener();
        listener.Listen(0, enableBroadcast: false, exclusiveAddressUse: true);
        int port = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint).Port;
        string loopback = IPAddress.Loopback.ToString();

        Assert.Throws<SocketException>(() =>
            listener.Listen(loopback, loopback, port, enableBroadcast: false, exclusiveAddressUse: true));
        Assert.Null(listener.Client);
        Assert.False(listener.IsMulticast);

        listener.Listen(port, enableBroadcast: false, exclusiveAddressUse: true);
        var reopenedClient = Assert.IsType<UdpClient>(listener.Client);
        Assert.Equal(port, Assert.IsType<IPEndPoint>(reopenedClient.Client.LocalEndPoint).Port);
    }

    [Fact]
    public void CanSwitchMulticastGroupsOnTheSamePort()
    {
        var firstGroup = IPAddress.Parse("239.255.0.111");
        var secondGroup = IPAddress.Parse("239.255.0.112");
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
        {
            try
            {
                probe.JoinMulticastGroup(firstGroup, IPAddress.Loopback);
                probe.JoinMulticastGroup(secondGroup, IPAddress.Loopback);
            }
            catch (SocketException exception)
            {
                Assert.Skip($"IPv4 multicast is unavailable on the loopback interface ({exception.SocketErrorCode}).");
            }
        }

        using var listener = new RxUdpListener();
        string localAddress = IPAddress.Loopback.ToString();
        listener.Listen(firstGroup.ToString(), localAddress, 0, enableBroadcast: false, exclusiveAddressUse: true);
        Assert.True(listener.IsMulticast);
        Assert.Equal(firstGroup, listener.MulticastAddress);
        var previousSocket = listener.Client.Client.SafeHandle;
        var endpoint = Assert.IsType<IPEndPoint>(listener.Client.Client.LocalEndPoint);

        listener.Listen(secondGroup.ToString(), localAddress, endpoint.Port, enableBroadcast: false, exclusiveAddressUse: true);
        Assert.True(previousSocket.IsClosed);
        Assert.True(listener.IsMulticast);
        Assert.Equal(secondGroup, listener.MulticastAddress);
        Assert.Equal(endpoint, listener.Client.Client.LocalEndPoint);

        listener.Close();
        Assert.Null(listener.Client);
        Assert.Null(listener.MulticastAddress);
        Assert.False(listener.IsMulticast);
    }
}
