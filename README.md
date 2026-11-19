# RxSocket [![NuGet](https://img.shields.io/nuget/v/Punio.RxSocket.svg)](https://www.nuget.org/packages/Punio.RxSocket/) [![License: MIT](http://img.shields.io/badge/license-MIT-blue.svg?style=flat)](https://github.com/punio/RxSocket/blob/master/LICENSE)

Socket wrapper for Rx

Targets .NET Standard 2.0.

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



## Tests

Run the .NET 10 test project from the repository root with `dotnet test RxSocket/RxSocket.sln`.
The tests use local loopback sockets and OS-assigned ports. Dual-stack DNS and multicast tests are skipped when their loopback requirements are unavailable.

## Performance measurements

Run the TCP receive benchmark from the repository root with the .NET 10 SDK.

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

## Author
[punio](https://github.com/punio)
