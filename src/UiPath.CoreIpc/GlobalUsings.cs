global using UiPath.Ipc.Extensibility;
global using BeforeConnectHandler = System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task>;
global using BeforeCallHandler = System.Func<UiPath.Ipc.CallInfo, System.Threading.CancellationToken, System.Threading.Tasks.Task>;
global using InvokeDelegate = System.Func<UiPath.Ipc.ServiceClient, System.Reflection.MethodInfo, object?[], object?>;
global using Accept = System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<System.Net.WebSockets.WebSocket>>;
global using Network = UiPath.Ipc.Extensibility.OneOf<UiPath.Ipc.IAsyncStream, System.IO.Stream>;
global using ContractToSettingsMap = System.Collections.Generic.Dictionary<string, UiPath.Ipc.EndpointSettings>;
global using AccessControlDelegate = System.Action<System.IO.Pipes.PipeSecurity>;
