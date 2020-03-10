// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
// tslint:disable: only-arrow-functions

import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import chaiAsPromised from 'chai-as-promised';
import * as path from 'path';

use(spies);
use(chaiAsPromised);

import { IpcClient } from '../../../src/core/surface';
import { TimeSpan } from '../../../src/foundation/threading';
import { Message } from '../../../src/core/surface/message';

import { DotNetScript } from './helpers/dotnet-script';

class IAlgebra {
    public MultiplySimple(x: number, y: number): Promise<number> {
        throw null;
    }
    public Multiply(x: number, y: number, message: Message<void>): Promise<number> {
        throw null;
    }
}

describe(`core:e2e -> interop between a NodeJS CoreIpc client and a .NET CoreIpc server`, function() {
    this.timeout(10 * 1000);

    let dotNetScript: DotNetScript = null as any;

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
    Task<int> MultiplySimple(int x, int y);
    Task<int> Multiply(int x, int y, Message message = default);
}

public sealed class Algebra : IAlgebra
{
    public Task<int> MultiplySimple(int x, int y)
    {
        return Task.FromResult(x * y);
    }

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

try {
    Console.WriteLine("###STARTING###");
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
} catch (Exception ex) {
    Console.WriteLine($"Exception: {ex.GetType().Name}\\r\\nMessage: {ex.Message}\\r\\nStack: {ex.StackTrace}");
}
`;
    }

    beforeEach(async () => {
        const relativePathTargetDir = process.env['NodeJS_NetStandardTargetDir_RelativePath'] || '..\\..\\UiPath.CoreIpc\\bin\\Debug\\netstandard2.0';
        const pathCwd = path.join(process.cwd(), relativePathTargetDir);

        const cSharpCode = getCSharpCode();
        dotNetScript = new DotNetScript(pathCwd, cSharpCode);

        await dotNetScript.waitForLineAsync('###DONE###');
        await Promise.delay(TimeSpan.fromSeconds(1));
    });

    afterEach(async () => {
        await dotNetScript.disposeAsync();
    });

    context(`e2e`, function() {
        it(`should support remote calls`, async function() {
            const client = new IpcClient('foobar', IAlgebra);

            const promise = client.proxy.MultiplySimple(3, 6);
            await expect(promise).to.eventually.equal(18);
        });

        it(`should support inlined callbacks`, async function() {
            this.timeout(10 * 1000);

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
        });
    });
});
