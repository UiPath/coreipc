using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace UiPath.Ipc;

public abstract class ServerTransport
{
    public int ConcurrentAccepts { get; init; } = 5;
    public byte MaxReceivedMessageSizeInMegabytes { get; init; } = 2;
    public X509Certificate? Certificate { get; init; }
    internal int MaxMessageSize => MaxReceivedMessageSizeInMegabytes * 1024 * 1024;

    // TODO: Maybe decommission.
    internal async Task<Stream> MaybeAuthenticate(Stream network)
    {
        if (Certificate is null)
        {
            return network;
        }

        var sslStream = new SslStream(network, leaveInnerStreamOpen: false);
        try
        {
            await sslStream.AuthenticateAsServerAsync(Certificate);
        }
        catch
        {
            sslStream.Dispose();
            throw;
        }

        Debug.Assert(sslStream.IsEncrypted && sslStream.IsSigned);
        return sslStream;
    }

    protected internal abstract IServerState CreateServerState();

    internal IEnumerable<string> Validate()
    => ValidateCore().Where(x => x is not null).Select(x => $"{GetType().Name}.{x}");
    protected abstract IEnumerable<string?> ValidateCore();
    protected static string? IsNotNull<T>(T? propertyValue, [CallerArgumentExpression(nameof(propertyValue))] string? propertyName = null)
    {
        if (propertyValue is null)
        {
            return $"{propertyName} is required.";
        }
        return null;
    }

    protected internal interface IServerState : IAsyncDisposable
    {
        IServerConnectionSlot CreateConnectionSlot();
    }
    protected internal interface IServerConnectionSlot : IDisposable
    {
        ValueTask<Stream> AwaitConnection(CancellationToken ct);
    }
}
