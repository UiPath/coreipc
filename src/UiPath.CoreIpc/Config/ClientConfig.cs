namespace UiPath.Ipc;

public sealed class ClientConfig : Peer, IServiceClientConfig
{
    public BeforeCallHandler? BeforeCall { get; init; }

    internal void Validate()
    {
        var haveDeferredInjectedCallbacks = Callbacks?.Any(x => x.Service.MaybeGetServiceProvider() is null && x.Service.MaybeGetInstance() is null) ?? false;

        if (haveDeferredInjectedCallbacks && ServiceProvider is null)
        {
            throw new InvalidOperationException("ServiceProvider is required when you register injectable callbacks. Consider registering a callback instance.");
        }
    }
}
