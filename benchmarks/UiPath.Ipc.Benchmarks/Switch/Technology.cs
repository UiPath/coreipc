namespace UiPath.Ipc.Benchmarks;

public abstract partial class Technology : IAsyncDisposable
{
    public abstract Task Init();
    public abstract ValueTask DisposeAsync();

    public abstract IProxy GetProxy();

    public interface IProxy
    {
        Task<float> AddFloats(float x, float y);
        Task<string> GetCallbackThreadName();
    }

    public interface IComputingCallback
    {
        Task<string> GetThreadName();
    }

    public sealed class ComputingCallback : IComputingCallback
    {
        public async Task<string> GetThreadName() => Thread.CurrentThread.Name!;
    }
}

