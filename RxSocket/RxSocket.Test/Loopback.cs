using System.Net;
using System.Net.Sockets;

namespace RxSocket.Test;

internal static class Loopback
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    internal static CancellationTokenSource CreateTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        return timeout;
    }

    internal static Socket BindTcpSocket(IPAddress address, int port = 0)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                socket.DualMode = false;

            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(address, port));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static async Task<Socket> ConnectTcpPeerAsync(EndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
