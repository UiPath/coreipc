global using UiPath.Ipc.Extensibility;
global using BeforeCallHandler = System.Func<UiPath.Ipc.CallInfo, System.Threading.CancellationToken, System.Threading.Tasks.Task>;
global using ConnectionFactory = System.Func<UiPath.Ipc.Connection?, System.Threading.CancellationToken, System.Threading.Tasks.Task<UiPath.Ipc.Connection>>;
global using InvokeDelegate = System.Func<UiPath.Ipc.ServiceClient, System.Reflection.MethodInfo, object?[], object?>;
global using Accept = System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<System.Net.WebSockets.WebSocket>>;
global using Network = UiPath.Ipc.Extensibility.OneOf<UiPath.Ipc.IAsyncStream, System.IO.Stream>;
global using ContractToSettingsMap = System.Collections.Generic.Dictionary<string, UiPath.Ipc.EndpointSettings>;