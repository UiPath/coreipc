// // tslint:disable: variable-name
// // tslint:disable: no-namespace
// // tslint:disable: no-internal-module

// import { runCsx } from '../csx-helpers';
// import {
//     PromisePal,
//     CancellationToken,
//     IpcClient,
//     Message,
//     __hasCancellationToken__,
//     __returns__
// } from '../../src/index';
// import { TimeSpan } from '../../src/foundation/tasks/timespan';

// describe('Foo', () => {

//     test('Bar2', async () => {
//         await runCsx(getBar2Csx(), async () => {
//             const client = new IpcClient('computingPipe', Types.IComputingService);
//             try {
//                 const actual = await client.proxy.AddComplexNumber(
//                     new Types.ComplexNumber(10, 20),
//                     new Types.ComplexNumber(30, 40)
//                 );

//                 expect(actual).toEqual(new Types.ComplexNumber(40, 60));
//             } finally {
//                 await client.closeAsync();
//             }
//         });

//     }, 1000 * 30);

//     function getBar2Csx(): string {
//         const csx = // javascript
//             `#! "netcoreapp2.0"
// #r ".\\Nito.AsyncEx.Context.dll"
// #r ".\\Nito.AsyncEx.Coordination.dll"
// #r ".\\Nito.AsyncEx.Interop.WaitHandles.dll"
// #r ".\\Nito.AsyncEx.Oop.dll"
// #r ".\\Nito.AsyncEx.Tasks.dll"
// #r ".\\Nito.Cancellation.dll"
// #r ".\\Nito.Collections.Deque.dll"
// #r ".\\Nito.Disposables.dll"
// #r ".\\Microsoft.Extensions.DependencyInjection.dll"
// #r ".\\UiPath.Ipc.dll"
// #r ".\\UiPath.Ipc.Tests.dll"

// using Microsoft.Extensions.DependencyInjection;
// using System;
// using System.Reflection;
// using System.IO;
// using System.Diagnostics;
// using System.Threading;
// using System.Threading.Tasks;
// using UiPath.Ipc;
// using UiPath.Ipc.NamedPipe;
// using UiPath.Ipc.Tests;

// // Debugger.Launch();

// var serviceProvider = new ServiceCollection()
//     .AddLogging()
//     .AddIpc()
//     .AddSingleton<IComputingService, ComputingService>()
//     .BuildServiceProvider();

// var host = new ServiceHostBuilder(serviceProvider)
//     .AddEndpoint(new NamedPipeEndpointSettings<IComputingService, IComputingCallback>("computingPipe") {
//         RequestTimeout = TimeSpan.FromSeconds(2)
//     })
//     .Build();

// await await Task.WhenAny(host.RunAsync(), Task.Run(async () =>
//     {
//         Console.WriteLine(typeof(int).Assembly);
//         await Task.Delay(500);
//         Console.WriteLine("#!READY");
//         Console.ReadLine();
//         host.Dispose();
//     }));

// Console.WriteLine("Server stopped.");
//         `;
//         return csx;
//     }

// });

// module Types {

//     export class SystemMessage extends Message<void> {
//         constructor(public Text: string, public Delay: number, requestTimeout: TimeSpan) {
//             super(requestTimeout);
//         }
//     }
//     export class ComplexNumber {
//         constructor(public A: number, public B: number) { }
//     }

//     export class IComputingService {
//         @__hasCancellationToken__
//         @__returns__(ComplexNumber)
//         // @ts-ignore
//         public AddComplexNumber(x: ComplexNumber, y: ComplexNumber, cancellationToken: CancellationToken = CancellationToken.none): Promise<ComplexNumber> { throw null; }

//         @__hasCancellationToken__
//         @__returns__(ComplexNumber)
//         // @ts-ignore
//         public AddComplexNumbers(numbers: ComplexNumber[], cancellationToken: CancellationToken = CancellationToken.none): Promise<ComplexNumber> { throw null; }

//         @__hasCancellationToken__
//         // @ts-ignore
//         public SendMessage(message: SystemMessage, cancellationToken: CancellationToken = CancellationToken.none): Promise<string> { throw null; }
//     }

//     export class IComputingCallback {
//         public GetId(): Promise<string> { throw null; }
//     }
//     export class ComputingCallback implements IComputingCallback {
//         public GetId(): Promise<string> {
//             return PromisePal.fromResult('foo');
//         }
//     }
// }
