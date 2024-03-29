﻿using System.Text;
using System.Diagnostics;
using UiPath.Rpc.WebSockets;
using Microsoft.Extensions.DependencyInjection;
namespace UiPath.Rpc.Tests;
class WebSocketClient
{
    static async Task _Main(string[] args)
    {
        Console.WriteLine(typeof(int).Assembly);
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        Thread.Sleep(1000);
        var source = new CancellationTokenSource();
        try
        {
            await await Task.WhenAny(RunTestsAsync(source.Token), Task.Run(() =>
            {
                Console.ReadLine();
                Console.WriteLine("Cancelling...");
                source.Cancel();
            }));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            Console.ReadLine();
        }
    }

    private static async Task RunTestsAsync(CancellationToken cancellationToken)
    {
        Uri uri = new("ws://localhost:1212/wsDemo/");
        var serviceProvider = ConfigureServices();
        var callback = new ComputingCallback { Id = "custom made" };
        var computingClientBuilder = new WebSocketClientBuilder<IComputingService, IComputingCallback>(uri, serviceProvider)
            .CallbackInstance(callback)/*.EncryptAndSign("localhost")*/.RequestTimeout(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();
        int count = 0;
        try
        {
            var computingClient = computingClientBuilder.ValidateAndBuild();
            var systemClient =
                new WebSocketClientBuilder<ISystemService>(uri)
                //.EncryptAndSign("localhost")
                .RequestTimeout(TimeSpan.FromSeconds(2))
                .Logger(serviceProvider)
                .ValidateAndBuild();
            var watch = Stopwatch.StartNew();
            //using (var file = File.OpenRead(@"C:\Windows\DPINST.log"))
            //{
            //    Console.WriteLine(await systemClient.Upload(file));
            //}
            for (int i =0; i<50;i++)
            {
                // test 1: call RPC service method with primitive types
                float result1 = await computingClient.AddFloat(1.23f, 4.56f, cancellationToken);
                count++;
                Console.WriteLine($"[TEST 1] sum of 2 floating number is: {result1}");
                // test 2: call RPC service method with complex types
                ComplexNumber result2 = await computingClient.AddComplexNumber(
                       new ComplexNumber(0.1f, 0.3f),
                       new ComplexNumber(0.2f, 0.6f), cancellationToken);
                Console.WriteLine($"[TEST 2] sum of 2 complexe number is: {result2.A}+{result2.B}i");

                // test 3: call RPC service method with an array of complex types
                ComplexNumber result3 = await computingClient.AddComplexNumbers(new[]
                {
                    new ComplexNumber(0.5f, 0.4f),
                    new ComplexNumber(0.2f, 0.1f),
                    new ComplexNumber(0.3f, 0.5f),
                }, cancellationToken);
                Console.WriteLine($"[TEST 3] sum of 3 complexe number is: {result3.A}+{result3.B}i", cancellationToken);

                // test 4: call RPC service method without parameter or return
                //await systemClient.DoNothing(cancellationToken);
                //Console.WriteLine($"[TEST 4] invoked DoNothing()");
                //((IDisposable)systemClient).Dispose();

                // test 5: call RPC service method with enum parameter
                string text = await systemClient.ConvertText("hEllO woRd!", TextStyle.Upper, cancellationToken);
                Console.WriteLine($"[TEST 5] {text}");

                // test 6: call RPC service method returning GUID
                Guid generatedId = await systemClient.GetGuid(Guid.NewGuid(), cancellationToken);
                Console.WriteLine($"[TEST 6] generated ID is: {generatedId}");

                // test 7: call RPC service method with byte array
                byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 1).Select(_ => "Test")));
                byte[] reversed = await systemClient.ReverseBytes(input, cancellationToken);
                Console.WriteLine($"[TEST 7] reverse bytes");

                // test 8: call RPC service method with callback
                var userName = await computingClient.SendMessage(new SystemMessage { Text = "client text" }, cancellationToken);
                Console.WriteLine($"[TEST 8] client identity : {userName}");

                //// test 9: call RPC service method with message parameter
                ////Console.WriteLine($"[TEST 9] callback error");
                //try
                //{
                //    //userName = await systemClient.SendMessage(new SystemMessage { Text = "client text" }, cancellationToken);
                //}
                //catch(Exception ex)
                //{
                //    //Console.WriteLineex.Message);
                //}
            }
            watch.Stop();
            Console.WriteLine(watch.ElapsedMilliseconds);
            var callbackProxy = (IDisposable)computingClient;
            callbackProxy.Dispose();
            callbackProxy.Dispose();
            callbackProxy.Dispose();
            //((RpcProxy)callbackProxy).CloseConnection();
            ((RpcProxy)computingClient).CloseConnection();
            ((RpcProxy)systemClient).CloseConnection();
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine();
            Console.WriteLine("Calls per second: " + count / stopwatch.Elapsed.TotalSeconds);
            Console.WriteLine();
        }
        // test 10: call slow RPC service method
        //await systemClient.SlowOperation(cancellationToken);
        //Console.WriteLine($"[TEST 10] Called slow operation");
    }

    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddRpcWithLogging()
            .BuildServiceProvider();
}