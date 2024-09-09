using Microsoft.EntityFrameworkCore;

namespace UiPath.Ipc.TV.DataAccess;

public class CallLogContext : DbContext
{
    public CallLogContext(DbContextOptions options) : base(options)
    {
    }

}

public sealed class OutgoingCallEntity
{
    public required string Id { get; init; }

    public required string Caller { get; set; }

    public required string Callee { get; set; }

    public string? InvokeRemoteProperSucceeded { get; set; }
    public string? InvokeRemoteProperFailed { get; set; }
}