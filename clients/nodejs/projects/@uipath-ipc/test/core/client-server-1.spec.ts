import '../csx-helpers';
import '../jest-extensions';
import { runCsx } from '../csx-helpers';
import { __returns__, CancellationToken, __hasCancellationToken__, Message, IpcClient } from '../../src';

describe('Client-Server-1', () => {

    test(`Main`, async () => {

        await runCsx(csx(), async () => {
            const client = new IpcClient('test-pipe', Contract.ITestService, config => {
                config.callbackService = new TestCallback();
            });
            try {
                const a = new Contract.Complex(1, 2);
                const b = new Contract.Complex(3, 4);
                const expected = new Contract.Complex(4, 6);

                await expect(client.proxy.AddAsync(a, new Message<Contract.Complex>(b, 5))).resolves.toEqual(expected);
            } finally {
                await client.closeAsync();
            }
        });

    }, 1000 * 30);

});

function csx(): string {
    const result = // javascript
        `#! "netcoreapp2.0"
#r ".\\Nito.AsyncEx.Context.dll"
#r ".\\Nito.AsyncEx.Coordination.dll"
#r ".\\Nito.AsyncEx.Interop.WaitHandles.dll"
#r ".\\Nito.AsyncEx.Oop.dll"
#r ".\\Nito.AsyncEx.Tasks.dll"
#r ".\\Nito.Cancellation.dll"
#r ".\\Nito.Collections.Deque.dll"
#r ".\\Nito.Disposables.dll"
#r ".\\Microsoft.Extensions.DependencyInjection.dll"
#r ".\\UiPath.Ipc.dll"
#r ".\\UiPath.Ipc.Tests.dll"

using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UiPath.Ipc;
using UiPath.Ipc.NamedPipe;
using UiPath.Ipc.Tests;

public static class Contract {
    public sealed class Complex {
        public double X { get; set; }
        public double Y { get; set; }
    }
    public interface ITestService {
        Task<Complex> AddAsync(Complex a, Message<Complex> b, CancellationToken ct = default);
    }
    public interface ITestCallback {
        Task<double> AddAsync(double a, double b);
    }
}
public class TestService : Contract.ITestService {
    public async Task<Contract.Complex> AddAsync(Contract.Complex a, Message<Contract.Complex> b, CancellationToken ct) {
        var callback = b.Client.GetCallback<Contract.ITestCallback>();
        var x = await callback.AddAsync(a.X, b.Payload.X);
        var y = await callback.AddAsync(a.Y, b.Payload.Y);
        return new Contract.Complex { X = x, Y = y };
    }
}

var serviceProvider = new ServiceCollection()
    .AddLogging()
    .AddIpc()
    .AddSingleton<Contract.ITestService, TestService>()
    .BuildServiceProvider();

var host = new ServiceHostBuilder(serviceProvider)
    .AddEndpoint(new NamedPipeEndpointSettings<Contract.ITestService, Contract.ITestCallback>("test-pipe") {
        RequestTimeout = TimeSpan.FromSeconds(2)
    })
    .Build();

await await Task.WhenAny(host.RunAsync(), Task.Run(async () =>
    {
        Console.WriteLine(typeof(int).Assembly);
        await Task.Delay(500);
        Console.WriteLine("#!READY");
        Console.ReadLine();
        host.Dispose();
    }));
`;
    return result;
}

// tslint:disable-next-line: no-namespace
namespace Contract {
    export class Complex {
        constructor(public readonly X: number, public readonly Y: number) { }
    }
    export class ITestService {
        @__hasCancellationToken__
        @__returns__(Complex)
        // @ts-ignore
        public AddAsync(a: Complex, b: Message<Complex>, ct?: CancellationToken): Promise<Complex> { throw null; }
    }
    export interface ITestCallback {
        AddAsync(a: number, b: number): Promise<number>;
    }
}

export class TestCallback implements Contract.ITestCallback {
    public async AddAsync(a: number, b: number): Promise<number> {
        return a + b;
    }
}
