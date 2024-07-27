global using UiPath.Ipc.Extensibility;
global using BeforeCallHandler = System.Func<UiPath.Ipc.CallInfo, System.Threading.CancellationToken, System.Threading.Tasks.Task>;
global using ConnectionFactory = System.Func<UiPath.Ipc.Extensibility.OneOf<UiPath.Ipc.IAsyncStream, System.IO.Stream>?, System.Threading.CancellationToken, System.Threading.Tasks.Task<UiPath.Ipc.Extensibility.OneOf<UiPath.Ipc.IAsyncStream, System.IO.Stream>?>>;
global using Network = UiPath.Ipc.Extensibility.OneOf<UiPath.Ipc.IAsyncStream, System.IO.Stream>;
