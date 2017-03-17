# RxSocket
Socket wrapper for Rx

## Usage

### RxTcpClient
```cs
var client = new RxTcpClient();
// Observe
client.Error.Subscribe();
client.Closed.Subscribe();
client.Received.Subscribe();

// Connect
try{
  client.Connect("127.0.0.1",10000);
}catch(Excepction){}
```

### RxTcpServer
```cs
var server = new RxTcpServer();
// Observe
server.Error.Subscribe();
server.Accepted.Subscribe();  // client connected
server.Closed.Subscribe();  // client closed
server.Received.Subscribe();

// Listen
try{
  server.Listen(IPAddress.Any.ToString(),10000);
}catch(Exception){}
```

## Licence
[MIT](https://github.com/tcnksm/tool/blob/master/LICENCE)


## Author
[punio](https://github.com/punio)
