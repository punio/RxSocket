# RxSocket ![NuGet](https://img.shields.io/nuget/v/Punio.RxSocket.svg)](https://www.nuget.org/packages/RxSocket/) ![License: MIT](http://img.shields.io/badge/license-MIT-blue.svg?style=flat)](https://github.com/punio/RxSocket/blob/master/LICENSE)

Socket wrapper for Rx

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
```cs
var listener = new RxUdpListener();
// Subscribe
listener.Received.Subscribe();

// Listen
try{
  listener.Listen(10000); // UNICAST
  // or 
  // listener.Listen("224.0.0.1",10000);  // MULTICAST
}catch(Exception){}
```



## Author
[punio](https://github.com/punio)
