#! "netcoreapp2.0"
#r ".\Nito.AsyncEx.Context.dll"
#r ".\Nito.AsyncEx.Coordination.dll"
#r ".\Nito.AsyncEx.Interop.WaitHandles.dll"
#r ".\Nito.AsyncEx.Oop.dll"
#r ".\Nito.AsyncEx.Tasks.dll"
#r ".\Nito.Cancellation.dll"
#r ".\Nito.Collections.Deque.dll"
#r ".\Nito.Disposables.dll"
#r ".\Microsoft.Extensions.DependencyInjection.dll"
#r ".\UiPath.Ipc.dll"
#r ".\UiPath.Ipc.Tests.dll"

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
    public async Task<Contract.Complex> AddAsync(Contract.Complex a, Message<Contract.Complex> b, CancellationToken ct) {
        var callback = b.Client.GetCallback<Contract.ITestCallback>();
        var x = await callback.AddAsync(a.X, b.Payload.X);
        var y = await callback.AddAsync(a.Y, b.Payload.Y);
        return new Contract.Complex { X = x, Y = y };
    }
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
