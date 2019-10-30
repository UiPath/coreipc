# CoreIpc
WCF-like service model API for communication over named pipes.
- async
- json serialization
- DI integration
- cancellation
- timeouts
- callbacks
- automatic reconnect
- interception
- custom task scheduler
- client impersonation

Check [the tests](https://github.com/UiPath/CoreIpc/blob/master/src/UiPath.CoreIpc.Tests/IpcTests.cs) and the sample.
```C#
// configure the server
var host = 
    new ServiceHostBuilder(serviceProvider)
    .AddEndpoint(new NamedPipeEndpointSettings<IComputingService>())
    .Build();
// start the server
_ = host.RunAsync();
// configure the client
var computingClient = 
    new NamedPipeClientBuilder<IComputingService>()
    .Build();
// call a remote method
var result = await computingClient.AddFloat(1.23f, 4.56f, cancellationToken);
```