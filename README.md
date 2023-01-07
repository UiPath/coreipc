[![Build Status](https://uipath.visualstudio.com/CoreIpc/_apis/build/status/CI?branchName=master)](https://uipath.visualstudio.com/CoreIpc/_build/latest?definitionId=637&branchName=master)
[![MyGet (dev)](https://img.shields.io/badge/CoreIpc-Preview-brightgreen)](https://uipath.visualstudio.com/Public.Feeds/_packaging?_a=package&feed=UiPath-Internal&view=versions&package=UiPath.CoreIpc&protocolType=NuGet)
# CoreIpc
WCF-like service model API for communication over named pipes, TCP and web sockets. .NET and [Node.js](src/Clients/nodejs) clients.
- async
- json serialization
- DI integration
- cancellation
- timeouts
- callbacks
- one way calls (all methods that return non-generic `Task`)
- automatic reconnect
- interception
- configurable task scheduler
- client authentication and impersonation
- access to the underlying transport with `Stream` parameters
- SSL

Check [the tests](https://github.com/UiPath/CoreIpc/blob/master/src/UiPath.CoreIpc.Tests/) and the sample.
```C#
// configure and start the server
_ = new ServiceHostBuilder(serviceProvider)
    .UseNamedPipes(new NamedPipeSettings("computing")) 
    .AddEndpoint<IComputingService>()
    .Build()
    .RunAsync();
// configure the client
var computingClient = 
    new NamedPipeClientBuilder<IComputingService>("computing")
    .Build();
// call a remote method
var result = await computingClient.AddFloat(1, 4, cancellationToken);
```
# UiPath.Rpc
[![Build Status](https://uipath.visualstudio.com/CoreIpc/_apis/build/status/CI?branchName=master)](https://uipath.visualstudio.com/CoreIpc/_build/latest?definitionId=3428&branchName=master)
[![MyGet (dev)](https://img.shields.io/badge/UiPath.Rpc-Preview-brightgreen)](https://uipath.visualstudio.com/Public.Feeds/_packaging?_a=package&feed=UiPath-Internal&view=versions&package=UiPath.Rpc&protocolType=NuGet)

https://github.com/UiPath/coreipc/tree/master/UiPath.Rpc
A more efficient version based on MessagePack.
# Debug using Source Link
[Preview builds setup](https://docs.microsoft.com/en-us/azure/devops/pipelines/artifacts/symbols?view=azure-devops#set-up-visual-studio).