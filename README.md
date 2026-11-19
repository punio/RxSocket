# RxSocket [![NuGet](https://img.shields.io/nuget/v/Punio.RxSocket.svg)](https://www.nuget.org/packages/Punio.RxSocket/) [![License: MIT](http://img.shields.io/badge/license-MIT-blue.svg?style=flat)](https://github.com/punio/RxSocket/blob/master/LICENSE)

Socket wrapper for Rx

Targets .NET 6 and later.

## Install
```
PM> Install-Package Punio.RxSocket 
```

## Usage

### RxTcpClient
```cs
var client = new RxTcpClient();
// Subscribe
client.Error.Subscribe();
client.Closed.Subscribe();
client.Received.Subscribe();

// Connect
try{
  client.Connect("127.0.0.1",10000);
}catch(Exception){}
```

### RxTcpServer
```cs
var server = new RxTcpServer();
// Subscribe
server.Error.Subscribe();
server.Accepted.Subscribe();  // client connected
server.Closed.Subscribe();  // client closed
server.Received.Subscribe();

// Listen
try{
  server.Listen(IPAddress.Any.ToString(),10000);
}catch(Exception){}
```

### RxUdpListener

Subscribe to `Error` to detect when a receive failure stops the listener.
Call `Listen` again to resume receiving with the same `Received` subscription.

```cs
var listener = new RxUdpListener();
// Subscribe
listener.Received.Subscribe();
listener.Error.Subscribe(error => Console.WriteLine(error.Exception.Message));

// Listen
try{
  listener.Listen(10000); // UNICAST
  // or 
  // listener.Listen("224.0.0.1",10000);  // MULTICAST
}catch(Exception){}
```



Set `ReceiveBufferSize` in bytes before calling `Listen` to let the socket queue more datagrams during bursts.
The default value of zero uses the operating system setting.

Use `DatagramReceived` to read the payload directly from the reusable receive buffer.
The span is valid only until the synchronous handler returns. Call `data.ToArray()` inside the handler if you need to retain it.
`Received` provides an independent payload array for each datagram, including when both APIs are used together.

```cs
using var listener = new RxUdpListener { ReceiveBufferSize = 4 * 1024 * 1024 };
listener.DatagramReceived += (from, data) => Console.WriteLine($"{from} sent {data.Length} bytes");
listener.Listen(10000);
```

## Tests

Run the .NET 10 test project from the repository root with `dotnet test RxSocket/RxSocket.sln`.
The tests use local loopback sockets and OS-assigned ports. Dual-stack DNS and multicast tests are skipped when their loopback requirements are unavailable.

## Performance measurements

Run both benchmarks sequentially in one process from the repository root with the .NET 10 SDK.

```powershell
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf-all
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf-all 1 3 100 --duplex --udp 1000 1200 3 100
```

Arguments before `--udp` apply to TCP. Arguments after it apply to UDP. Each benchmark uses its defaults when its arguments are omitted.

### TCP

```powershell
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf
```

The benchmark compares 1, 4 and 16 KiB receive buffers over IPv4 loopback. The default is one client sending 64 MiB per measured run, with three runs per buffer size after a warm-up. The positional arguments set the MiB per client, runs per buffer, and number of clients.

```powershell
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf 1 3 100
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf 1 3 100 --duplex
```

Each run starts transfers after all clients connect. With `--duplex`, the accepted server sockets also send the specified payload to each client while the server receives through RxSocket. The benchmark checks that each connection transfers the expected byte count.

Each row reports aggregate throughput in MiB/s, managed bytes allocated per MiB transferred, and the number of server receive notifications. Duplex totals include both directions. Allocations cover the entire benchmark process.

### UDP

```powershell
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf-udp
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf-udp 1000 1200 3 100
dotnet run --project RxSocket/TestApp/TestApp.csproj -c Release -- --perf-udp 1000 1200 3 100 --fast --receive-buffer=4194304
```

The UDP benchmark sends IPv4 loopback datagrams to one `RxUdpListener`. The arguments set the datagrams per sender, payload size in bytes, run count, and sender count. The default is 50,000 datagrams of 1,200 bytes per run with one sender and three measured runs after a warm-up.

`--fast` measures the `DatagramReceived` API and `--receive-buffer=bytes` requests a socket receive buffer size. The output shows the size reported by the socket.

Each run reports sent and received datagrams, loss percentage, receive throughput, and process-wide managed allocations per MiB received. Datagrams are counted once by sequence number. The receiver can drain for up to 250 ms after sending finishes. Throughput uses the later of the final send and final receive as the end of the measured interval.

## Author
[punio](https://github.com/punio)
