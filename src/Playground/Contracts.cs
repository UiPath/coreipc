using UiPath.Ipc;

namespace Playground;

public static class Contracts
{
    public const string PipeName = "SomePipe";

    public interface IServerOperations
    {
        Task<bool> Register(Message? m = null);
        Task<bool> Broadcast(string text);
    }

    public interface IClientOperations
    {
        Task<bool> Greet(string text);
    }

    public interface IClientOperations2
    {
        Task<DateTime> GetTheTime();
    }
}
