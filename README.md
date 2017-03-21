# RxSocket
Socket wrapper for Rx

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
}catch(Excepction){}
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


## Licence
[MIT](https://github.com/punio/RxSocket/blob/master/LICENSE)


## Author
[punio](https://github.com/punio)
