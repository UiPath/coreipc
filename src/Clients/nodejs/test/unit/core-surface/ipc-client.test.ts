// tslint:disable: max-line-length
// tslint:disable: no-unused-expression

import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import chaiAsPromised from 'chai-as-promised';
import * as path from 'path';

use(spies);
use(chaiAsPromised);

import { IpcClient } from '../../../src/core/surface';
import { IpcClientConfig } from '../../../src/core/surface/ipc-client';
import { IBroker } from '../../../src/core/internals/broker';
import { ArgumentNullError } from '../../../src/foundation/errors';
import { PromiseCompletionSource, CancellationToken, TimeSpan } from '../../../src/foundation/threading';
import { IPipeClientStream } from '../../../src/foundation/pipes';
import { Message } from '../../../src/core/surface/message';

import * as BrokerMessage from '../../../src/core/internals/broker-message';
import { DotNetScript } from './helpers/dotnet-script';

class IAlgebra {
    public Multiply(x: number, y: number, message: Message<void>): Promise<number> {
        throw null;
    }
}

describe(`core:surface -> class:IpcClient`, () => {
    // tslint:disable-next-line: only-arrow-functions
    context(`e2e`, function() {
        this.timeout(10 * 1000);

        it(`should work`, async () => {
            const relativePathTargetDir = process.env['NodeJS_NetStandardTargetDir_RelativePath'] || '..\\..\\UiPath.CoreIpc\\bin\\Debug\\netstandard2.0';
            const pathCwd = path.join(process.cwd(), relativePathTargetDir);

            function getCSharpCode() {
                return /* javascript */ `
#r "nuget: Microsoft.Extensions.DependencyInjection.Abstractions, 3.0.0"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 3.0.0"

#r "nuget: Microsoft.Extensions.DependencyInjection, 3.0.0"
#r "nuget: Microsoft.Extensions.Logging, 3.0.0"

#r "nuget: Nito.AsyncEx.Context, 5.0.0"
#r "nuget: Nito.AsyncEx.Tasks, 5.0.0"
#r "nuget: Nito.AsyncEx.Coordination, 5.0.0"

#r "UiPath.CoreIpc.dll"

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx;
using UiPath.CoreIpc;
using UiPath.CoreIpc.NamedPipe;

// Debugger.Launch();

public interface IArithmetics
{
    Task<int> Sum(int x, int y);
}

public interface IAlgebra
{
    Task<int> Multiply(int x, int y, Message message = default);
}

public sealed class Algebra : IAlgebra
{
    public async Task<int> Multiply(int x, int y, Message message = default)
    {
        var arithmetics = message.GetCallback<IArithmetics>();

        int result = 0;
        for (int i = 0; i < x; i++)
        {
            result = await arithmetics.Sum(result, y);
        }

        return result;
    }
}

var services = new ServiceCollection();

var sp = services
    .AddLogging()
    .AddIpc()
    .AddSingleton<IAlgebra, Algebra>()
    .BuildServiceProvider();

var serviceHost = new ServiceHostBuilder(sp)
    .UseNamedPipes(new NamedPipeSettings("foobar"))
    .AddEndpoint<IAlgebra, IArithmetics>()
    .Build();

var thread = new AsyncContextThread();
thread.Context.SynchronizationContext.Send(_ => Thread.CurrentThread.Name = "GuiThread", null);
var sched = thread.Context.Scheduler;
Console.WriteLine("###DONE###");
await serviceHost.RunAsync(sched);
`;
            }

            const cSharpCode = getCSharpCode();

            const dotNetScript = new DotNetScript(pathCwd, cSharpCode);
            try {
                await dotNetScript.waitForLineAsync('###DONE###');
                await Promise.delay(TimeSpan.fromSeconds(1));

                interface IArithmetics {
                    Sum(x: number, y: number): Promise<number>;
                }

                class Arithmetics implements IArithmetics {
                    public async Sum(x: number, y: number): Promise<number> {
                        return x + y;
                    }
                }
                const arithmetics = new Arithmetics();
                arithmetics.Sum = spy(arithmetics.Sum);

                const client = new IpcClient('foobar', IAlgebra, x => {
                    x.callbackService = arithmetics;
                });
                const result = await client.proxy.Multiply(3, 6, new Message<void>());

                expect(result).to.equal(18);
                expect(arithmetics.Sum).to.have.been.called.exactly(3);
            } finally {
                await dotNetScript.disposeAsync();
            }
        });
    });

    context(`ctor`, () => {
        it(`should throw provided a falsy pipeName`, () => {
            (() => new IpcClient(null as any, Object)).should.throw(ArgumentNullError).with.property('paramName', 'pipeName');
            (() => new IpcClient(undefined as any, Object)).should.throw(ArgumentNullError).with.property('paramName', 'pipeName');
            (() => new IpcClient('', Object)).should.throw(ArgumentNullError).with.property('paramName', 'pipeName');
        });

        it(`should throw provided a falsy serviceCtor`, () => {
            (() => new IpcClient('pipeName', null as any)).should.throw(ArgumentNullError).with.property('paramName', 'serviceCtor');
            (() => new IpcClient('pipeName', undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'serviceCtor');
        });

        it(`shouldn't throw provided valid args`, () => {
            (() => new IpcClient('pipeName', Object)).should.not.throw();
            (() => new IpcClient('pipeName', Object, config => { })).should.not.throw();
        });
    });

    context(`property:proxy`, () => {
        it(`shouldn't throw`, () => {
            (() => new IpcClient('pipeName', Object).proxy).should.not.throw();
        });

        it(`shouldn't be null or undefined`, () => {
            expect(new IpcClient('pipeName', Object).proxy).not.to.be.null.and.not.to.be.undefined;
        });

        it(`should return the same object over and over`, () => {
            const ipcClient = new IpcClient('pipeName', Object);
            ipcClient.proxy.should.equal(ipcClient.proxy);
        });

        it(`should return a transparent proxy which marshalls through the underlying broker`, async () => {
            class IContract {
                public sumAsync(x: number, y: number): Promise<number> { throw null; }
            }

            const pcsCall = new PromiseCompletionSource<{
                brokerRequest: BrokerMessage.Request
                pcsRespond: PromiseCompletionSource<BrokerMessage.Response>
            }>();

            const mockBroker: IBroker = {
                async sendReceiveAsync(brokerRequest: BrokerMessage.Request) {
                    const pcsRespond = new PromiseCompletionSource<BrokerMessage.Response>();
                    pcsCall.setResult({ brokerRequest, pcsRespond });
                    return await pcsRespond.promise;
                },
                async disposeAsync() {
                }
            };

            const ipcClient = new IpcClient(
                'pipeName',
                IContract,
                config => {
                    (config as any as IpcClientConfig).maybeBroker = mockBroker;
                }
            );

            let promise: Promise<number> = null as any;
            (() => promise = ipcClient.proxy.sumAsync(1, 2)).should.not.throw();

            expect(promise).to.be.instanceOf(Promise);

            const spyPcsCallFulfilled = spy(() => { });
            pcsCall.promise.then(spyPcsCallFulfilled);
            await Promise.yield();
            spyPcsCallFulfilled.should.have.been.called();

            const obj = await pcsCall.promise;
            expect(obj).not.to.be.null.and.not.be.undefined;
            expect(obj.brokerRequest).to.be.instanceOf(BrokerMessage.OutboundRequest);
            expect(obj.pcsRespond).to.be.instanceOf(PromiseCompletionSource);
            expect(obj.brokerRequest.methodName).to.equal('sumAsync');
            expect(obj.brokerRequest.args).to.deep.equal([1, 2]);

            obj.pcsRespond.setResult(new BrokerMessage.Response(3, null));
            await promise.should.eventually.be.fulfilled.and.be.equal(3);
        });
    });

    context(`method:closeAsync`, () => {
        it(`shouldn't reject even if called multiple times`, async () => {
            const ipcClient = new IpcClient('pipeName', Object);
            await ipcClient.closeAsync().should.eventually.be.fulfilled;
            await ipcClient.closeAsync().should.eventually.be.fulfilled;
        });
    });
});

describe(`core:surface -> class:IpcClientConfig`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new IpcClientConfig()).should.not.throw();
        });
    });

    context(`method:setConnectionFactory`, () => {
        it(`should throw provided a falsy delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setConnectionFactory(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
            (() => instance.setConnectionFactory(undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
        });

        it(`shouldn't throw provided a valid delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setConnectionFactory(async (connect, ct) => { })).should.not.throw();
        });

        it(`should set the maybeConnectionFactory field`, () => {
            const instance = new IpcClientConfig();
            const method = async (connect: () => Promise<IPipeClientStream>, cancellationToken: CancellationToken): Promise<IPipeClientStream | void> => { };

            instance.setConnectionFactory(method);
            expect(instance.maybeConnectionFactory).to.equal(method);
        });
    });

    context(`method:setBeforeCall`, () => {
        it(`should throw provided a falsy delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setBeforeCall(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
            (() => instance.setBeforeCall(undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'delegate');
        });

        it(`shouldn't throw provided a valid delegate`, () => {
            const instance = new IpcClientConfig();

            (() => instance.setBeforeCall(async (connect, ct) => { })).should.not.throw();
        });

        it(`should set the maybeBeforeCall field`, () => {
            const instance = new IpcClientConfig();
            const method = async (methodName: string, newConnection: boolean, cancellationToken: CancellationToken): Promise<void> => { };

            instance.setBeforeCall(method);
            expect(instance.maybeBeforeCall).to.equal(method);
        });
    });
});
