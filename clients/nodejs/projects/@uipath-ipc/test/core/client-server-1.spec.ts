import '../csx-helpers';
import '../jest-extensions';
import * as path from 'path';
import { runCsx } from '../csx-helpers';
import {
    IpcClient,
    Message,
    CancellationToken,
    CancellationTokenSource,
    __returns__,
    __hasCancellationToken__,
    Timeout,
    TimeSpan,
    PromisePal,
    OperationCanceledError,
    Trace
} from '../../src';

import {
    DotNetScript
} from '@uipath/test-helpers';
import { TimeoutError } from '../../src/foundation/errors/timeout-error';

describe('Client-Server-1', () => {

    test(`Infinite`, async () => {

        Trace.addListener(x => {
            console.log('Trace: ', x);
        });
        await runCsx(csx(), async () => {
            const client = new IpcClient('test-pipe', Contract.ITestService, config => {
                config.callbackService = new TestCallback();
            });
            try {
                const promise1 = client.proxy.InfiniteAsync(new Message<void>(TimeSpan.fromMilliseconds(10)));
                await expect(promise1).rejects.toBeInstanceOf(OperationCanceledError);

                const cts = new CancellationTokenSource();
                const promise = client.proxy.InfiniteAsync(new Message<void>(Timeout.infiniteTimeSpan), cts.token);
                const _then = jest.fn();
                const _catch = jest.fn(x => {
                    expect(x).toBeInstanceOf(OperationCanceledError);
                });
                promise.then(_then, _catch);

                await PromisePal.delay(TimeSpan.fromSeconds(5));
                expect(_then).not.toHaveBeenCalled();
                expect(_catch).not.toHaveBeenCalled();

                cts.cancel();
                await PromisePal.yield();
                expect(_then).not.toHaveBeenCalled();
                expect(_catch).toHaveBeenCalledTimes(1);
            } finally {
                await client.closeAsync();
            }
        });

    }, 1000 * 30);

    test(`timeout`, async () => {

        class ITestService {
            public InfiniteAsync(message: Message<void>, ct: CancellationToken): Promise<boolean> { throw null; }
        }

        function getCSharpContract(): string {
            const result = // javascript
                `
public static class Contract {
    public interface ITestService {
        Task<bool> InfiniteAsync(Message message, CancellationToken ct = default);
    }
}
            `;
            return result;
        }

        function getCSharpImplementation(): string {
            const result = // javascript
                `
            public sealed class TestService : Contract.ITestService {
                public async Task<bool> InfiniteAsync(Message message, CancellationToken ct = default) {
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                    return true;
                }
            }
            `;
            return result;
        }

        function getCSharpScript(): string {
            const result = // javascript
                `
                ${getCSharpContract()}
                ${getCSharpImplementation()}

            var serviceProvider = new ServiceCollection()
                .AddLogging()
                .AddIpc()
                .AddSingleton<Contract.ITestService, TestService>()
                .BuildServiceProvider();

            var host = new ServiceHostBuilder(serviceProvider)
                .AddEndpoint(new NamedPipeEndpointSettings<Contract.ITestService>("test-pipe") {
                    RequestTimeout = TimeSpan.FromSeconds(10)
                })
                .Build();

            await await Task.WhenAny(host.RunAsync(), Task.Run(async () => {
                Console.WriteLine(typeof (int).Assembly);
                await Task.Delay(500);
                Console.WriteLine("#!READY");
                Console.ReadLine();
                host.Dispose();
            }));
            `;
            return result;
        }

        let dns: DotNetScript | null = null;
        let client: IpcClient<ITestService> | null = null;

        try {
            dns = await DotNetScript.startAsync(
                formatCsx(getCSharpScript()),
                path.join(process.cwd(), '$tools\\dotnet-script\\dotnet-script.cmd'),
                path.join(process.cwd(), '$uipath-ipc-dotnet')
            );
            client = new IpcClient('test-pipe', ITestService);

            await dns.waitAsync(x => x.type === 'line' && x.line === '#!READY');
            const cts = new CancellationTokenSource();
            cts.cancelAfter(TimeSpan.fromMilliseconds(500));

            await expect(client
                .proxy
                .InfiniteAsync(new Message<void>(), cts.token)
            ).rejects.toBeInstanceOf(OperationCanceledError);
        } finally {
            if (dns) {
                await dns.disposeAsync();
            }
        }
    }, 10 * 1000);

});

function formatCsx(code: string): string {
    // javascript
    return `#! "netcoreapp2.0"
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

${code.replace('\\', '\\\\')}
`;
}

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
        Task<bool> InfiniteAsync(Message message, CancellationToken ct = default);
        }
    public interface ITestCallback {
        Task<double> AddAsync(double a, double b);
    }
}
public class TestService : Contract.ITestService {
    public async Task < Contract.Complex > AddAsync(Contract.Complex a, Message < Contract.Complex > b, CancellationToken ct) {
        var callback = b.Client.GetCallback<Contract.ITestCallback>();
        var x = await callback.AddAsync(a.X, b.Payload.X);
        var y = await callback.AddAsync(a.Y, b.Payload.Y);
        return new Contract.Complex { X = x, Y = y };
    }
    public async Task < bool > InfiniteAsync(Message message, CancellationToken ct = default ) {
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
        return true;
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

await await Task.WhenAny(host.RunAsync(), Task.Run(async () => {
    Console.WriteLine(typeof (int).Assembly);
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

        @__hasCancellationToken__
        // @ts-ignore
        public InfiniteAsync(message: Message<void>, ct?: CancellationToken): Promise<boolean> { throw null; }
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
