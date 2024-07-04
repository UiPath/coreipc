using System.Collections;
using System.Net;

namespace UiPath.Ipc;

public static class IpcClient
{
    public static IpcClientConfiguration Config { get; } = IpcClientConfiguration.Instance;
    public static T Connect<T>(ChannelBase channel) where T : class
    {
        throw null!;
    }
}

public sealed class IpcClientConfiguration
{
    internal static readonly IpcClientConfiguration Instance = new();

    private readonly object _lock = new();
    private readonly Dictionary<ChannelBase, IpcClientOptions?> _options = new();

    public IpcClientOptions? this[ChannelBase channel]
    {
        get
        {
            lock (_lock)
            {
                _ = _options.TryGetValue(channel, out var result);
                return result;
            }
        }
        set
        {
            lock (_lock)
            {
                _options[channel] = value;
            }
        }
    }
}

public readonly record struct IpcClientOptions
{
    public IServiceProvider? ServiceProvider { get; init; }
    public EndpointCollection? Callbacks { get; init; }
    public ILogger? Logger { get; init; }
}

public class EndpointCollection : IEnumerable
{
    internal readonly Dictionary<Type, object?> Endpoints = new();

    public void Add(Type type) => Add(type, instance: null);
    public void Add<T>(T instance) where T : class => Add(typeof(T), instance);
    public void Add(Type type, object? instance)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (instance is not null && !instance.GetType().IsAssignableTo(type)) throw new ArgumentOutOfRangeException(nameof(instance));
        Endpoints[type] = instance;
    }

    IEnumerator IEnumerable.GetEnumerator() => Endpoints.GetEnumerator();
}

public abstract record ChannelBase
{
}
public sealed record NamedPipeChannel : ChannelBase
{
    public string PipeName { get; }

    public NamedPipeChannel(string pipeName) => PipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
}
public sealed record TCPChannel : ChannelBase
{
    public IPEndPoint EndPoint { get; }

    public TCPChannel(IPEndPoint endPoint) => EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
}
public sealed record WebSocketChannel : ChannelBase
{
    public Uri Uri { get; }

    public WebSocketChannel(Uri uri) => Uri = uri ?? throw new ArgumentNullException(nameof(uri));
}

