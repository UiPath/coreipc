global using Accept = System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<System.Net.WebSockets.WebSocket>>;
global using BeforeConnectHandler = System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task>;
global using BeforeCallHandler = System.Func<UiPath.Ipc.CallInfo, System.Threading.CancellationToken, System.Threading.Tasks.Task>;
