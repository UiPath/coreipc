using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace UiPath.Ipc;

public abstract class ServerTransport
{
    private protected ServerTransport() { }

    public int ConcurrentAccepts { get; set; } = 5;
    public byte MaxReceivedMessageSizeInMegabytes { get; set; } = 2;

    // TODO: Will be decommissioned altogether.
    internal X509Certificate? Certificate { get; init; }

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

    internal abstract IServerState CreateServerState();

    internal IEnumerable<string> Validate()
    => ValidateCore().Where(x => x is not null).Select(x => $"{GetType().Name}.{x}");
    internal abstract IEnumerable<string?> ValidateCore();
    internal static string? IsNotNull<T>(T? propertyValue, [CallerArgumentExpression(nameof(propertyValue))] string? propertyName = null)
    {
        if (propertyValue is null)
        {
            return $"{propertyName} is required.";
        }
        return null;
    }

    internal interface IServerState : IAsyncDisposable
    {
        IServerConnectionSlot CreateConnectionSlot();
    }
    internal interface IServerConnectionSlot : IAsyncDisposable
    {
        ValueTask<Stream> AwaitConnection(CancellationToken ct);
    }
}
