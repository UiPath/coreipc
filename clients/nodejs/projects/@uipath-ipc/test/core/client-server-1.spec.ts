// tslint:disable: variable-name
import '../jest-extensions';
import * as path from 'path';
import {
    IpcClient,
    Message,
    CancellationToken,
    CancellationTokenSource,
    __returns__,
    __hasCancellationToken__,
    TimeSpan,
    OperationCanceledError
} from '../../src';

import {
    DotNetScript
} from '@uipath/test-helpers';

describe('Client-Server-1', () => {
    class Setup {
        public static _null<T>(sample: () => T): IpcClient<T> | null {
            return null;
        }
        public static DotNet = class {
            public static format(contract: string, logic: string): string {
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

                     ${contract.replace('\\', '\\\\')}
                     ${logic.replace('\\', '\\\\')}
                    `;
                return result;
            }
        };
        public static Infinite = class {
            public static EcmaScript = class {
                public static ITestService = class {
                    public InfiniteAsync(message: Message<void>, ct: CancellationToken): Promise<boolean> { throw null; }
                };
            };
            public static DotNet = class {
                private static readonly _contract = // javascript
                    `public static class Contract {
                        public interface ITestService {
                            Task<bool> InfiniteAsync(Message message, CancellationToken ct = default);
                        }
                    }
                    `;

                public static readonly script = (() => {
                    // #region " contract "
                    const contract = // javascript
                        `public static class Contract {
                            public interface ITestService {
                                Task<bool> InfiniteAsync(Message message, CancellationToken ct = default);
                            }
                        }
                        `;
                    // #endregion
                    // #region " logic "
                    const logic = // javascript
                        `public sealed class TestService : Contract.ITestService {
                            public async Task<bool> InfiniteAsync(Message message, CancellationToken ct = default) {
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
                    // #endregion

                    return Setup.DotNet.format(contract, logic);
                })();
            };
        };
    }

    test(`Infinite call times out`, async () => {
        let dns: DotNetScript | null = null;
        let client = Setup._null(() => new Setup.Infinite.EcmaScript.ITestService());

        try {
            dns = await DotNetScript.startAsync(
                Setup.Infinite.DotNet.script,
                path.join(process.cwd(), '$tools\\dotnet-script\\dotnet-script.cmd'),
                path.join(process.cwd(), '$uipath-ipc-dotnet')
            );
            client = new IpcClient('test-pipe', Setup.Infinite.EcmaScript.ITestService);

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

    test(`Infinite call is canceled`, async () => {
        let dns: DotNetScript | null = null;
        let client = Setup._null(() => new Setup.Infinite.EcmaScript.ITestService());

        try {
            dns = await DotNetScript.startAsync(
                Setup.Infinite.DotNet.script,
                path.join(process.cwd(), '$tools\\dotnet-script\\dotnet-script.cmd'),
                path.join(process.cwd(), '$uipath-ipc-dotnet')
            );
            client = new IpcClient('test-pipe', Setup.Infinite.EcmaScript.ITestService);

            await dns.waitAsync(x => x.type === 'line' && x.line === '#!READY');
            const cts = new CancellationTokenSource();

            const promise = client
                .proxy
                .InfiniteAsync(new Message<void>(), cts.token);
            const _then = jest.fn();
            const _else = jest.fn();
            promise.then(_then, _else);

            cts.cancel();
            await Promise.yield();

            expect(_else).toHaveBeenCalledWith(new OperationCanceledError());
        } finally {
            if (dns) {
                await dns.disposeAsync();
            }
        }
    }, 10 * 1000);
});
