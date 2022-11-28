using System.Threading.Tasks.Sources;
namespace UiPath.CoreIpc;
internal static class TaskCompletionPool<T>
{
    public static ManualResetValueTaskSource Rent() => ObjectPool<ManualResetValueTaskSource>.TryRent() ?? new();
    static void Return(ManualResetValueTaskSource source) => ObjectPool<ManualResetValueTaskSource>.Return(source);
    public sealed class ManualResetValueTaskSource : IValueTaskSource<T>, IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<T> _core; // mutable struct; do not make this readonly
        public bool RunContinuationsAsynchronously { get => _core.RunContinuationsAsynchronously; set => _core.RunContinuationsAsynchronously = value; }
        public short Version => _core.Version;
        public ValueTask<T> ValueTask() => new(this, Version);
        public void Reset() => _core.Reset();
        public void SetResult(T result) => _core.SetResult(result);
        public void SetException(Exception error) => _core.SetException(error);
        public void SetCanceled() => _core.SetException(new TaskCanceledException());
        public T GetResult(short token) => _core.GetResult(token);
        void IValueTaskSource.GetResult(short token) => _core.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
        public void Return()
        {
            Reset();
            TaskCompletionPool<T>.Return(this);
        }
    }
}