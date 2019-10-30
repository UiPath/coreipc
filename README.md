# CoreIpc
WCF-like service model API for communication over named pipes.

```C#
    // configure the server
    var host = new ServiceHostBuilder(serviceProvider)
        .AddEndpoint(new NamedPipeEndpointSettings<IComputingService, IComputingCallback>("computingPipe"))
        .Build();
    // start the server
    _ = host.RunAsync();
    // configure the client
    var computingClient = new NamedPipeClientBuilder<IComputingService, IComputingCallback>("computingPipe", serviceProvider).Build();
    // call a remote method
    var result = await computingClient.AddFloat(1.23f, 4.56f, cancellationToken);
```