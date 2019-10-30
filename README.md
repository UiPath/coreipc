# CoreIpc
WCF-like service model API for communication over named pipes.
Check [the tests](https://github.com/UiPath/CoreIpc/blob/master/src/UiPath.CoreIpc.Tests/IpcTests.cs) for supported features.
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