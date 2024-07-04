using System.Security.Cryptography.X509Certificates;

namespace UiPath.Ipc;

public class ListenerSettingsBase
{
    public required string Name { get; init; }

    public byte ConcurrentAccepts { get; set; } = 5;
    public byte MaxReceivedMessageSizeInMegabytes { get; set; } = 2;
    public X509Certificate? Certificate { get; set; }
    public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;
}

public abstract class ListenerSettings : ListenerSettingsBase
{
    internal RouterConfig RouterConfig { get; set; }
}
abstract class Listener : IDisposable
{
    public string Name => Settings.Name;
    public ILogger? Logger { get; private set; }
    public ListenerSettings Settings { get; }
    public int MaxMessageSize { get; }

    protected Listener(ListenerSettings settings)
    {
        Settings = settings;
        MaxMessageSize = settings.MaxReceivedMessageSizeInMegabytes * 1024 * 1024;
    }

    public Task Listen(IServiceProvider serviceProvider, CancellationToken token)
    {
        Logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        if (LogEnabled)
        {
            Log($"Starting listener {Name}...");
        }
        return Task.WhenAll(Enumerable.Range(1, Settings.ConcurrentAccepts).Select(async _ =>
        {
            while (!token.IsCancellationRequested)
            {
                await AcceptConnection(serviceProvider, token);
            }
        }));
    }
    protected abstract ServerConnection CreateServerConnection();
    async Task AcceptConnection(IServiceProvider serviceProvider, CancellationToken token)
    {
        var serverConnection = CreateServerConnection();
        try
        {
            var network = await serverConnection.AcceptClient(token);
            serverConnection.Listen(serviceProvider, network, token).LogException(Logger, Name);
        }
        catch (Exception ex)
        {
            serverConnection.Dispose();
            if (!token.IsCancellationRequested)
            {
                Logger.LogException(ex, Settings.Name);
            }
        }
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }
        Settings.Certificate?.Dispose();
    }
    public void Dispose()
    {
        if (LogEnabled)
        {
            Log($"Stopping listener {Name}...");
        }
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    public void Log(string message) => Logger.LogInformation(message);
    public bool LogEnabled => Logger.Enabled();
}