[![Build Status](https://uipath.visualstudio.com/CoreIpc/_apis/build/status/CI?branchName=master)](https://uipath.visualstudio.com/CoreIpc/_build/latest?definitionId=637&branchName=master)
# CoreIpc client for Node.js

Service model API allowing a Node.js application to consume services exposed by a [.NET CoreIpc](https://github.com/UiPath/coreipc) server.

Check [the tests](test/unit/core-surface/ipc-client.test.ts) and the sample:

---

Consuming a service exposed by a [.NET CoreIpc](https://github.com/UiPath/coreipc) server, such as:

```C#
// given the contract:
public struct ComplexNumber
{
    public float A { get; set; }
    public float B { get; set; }
}

public interface IComputingService
{
    Task<ComplexNumber> AddComplexNumber(ComplexNumber x, ComplexNumber y, CancellationToken cancellationToken = default);
}

// configure and start the server
_ = new ServiceHostBuilder(serviceProvider)
    .UseNamedPipes(new NamedPipeSettings("computing")) 
    .AddEndpoint<IComputingService>()
    .Build()
    .RunAsync();
```

can be easily done in Node.js:

```TypeScript
// declare the contract in TypeScript:
export class ComplexNumber {
    constructor(public A: number, public B: number) { }
}

export class IComputingService {
    @__hasCancellationToken__
    public AddComplexNumber(x: ComplexNumber, y: ComplexNumber, cancellationToken?: CancellationToken): Promise<ComplexNumber> { throw null; }
}

// instantiate the `IpcClient<TService>` class and use the `proxy` property to call the service
const ipcClient = new IpcClient('computing', IComputingService);
const result = await ipcClient.proxy.AddComplexNumber(new ComplexNumber(1, 2), new ComplexNumber(10, 100));
console.log(`expecting [11, 102], got [${result.A}, ${result.B}]`);
```
