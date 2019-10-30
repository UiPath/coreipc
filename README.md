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
Check [the tests](https://github.com/UiPath/CoreIpc/blob/master/src/UiPath.CoreIpc.Tests/IpcTests.cs) and the sample.
```C#
// configure the server
var host = 
    new ServiceHostBuilder(serviceProvider)
    .AddEndpoint(new NamedPipeEndpointSettings<IComputingService>("computingPipe"))
    .Build();
// start the server
_ = host.RunAsync();
// configure the client
var computingClient = 
    new NamedPipeClientBuilder<IComputingService>("computingPipe")
    .Build();
// call a remote method
var result = await computingClient.AddFloat(1.23f, 4.56f, cancellationToken);
```