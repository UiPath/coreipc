using Nito.AsyncEx;
using System.Runtime.CompilerServices;

namespace UiPath.Ipc.Tests;

internal sealed class Callbacks<T> where T : class
{
    private readonly List<Callback<T>> _callbacks = new List<Callback<T>>();

    public bool Any() => _callbacks.Count != 0;

    public bool TryRegister(Message message, out Callback<T> callback) // false if already registered
    {
        callback = _callbacks.FirstOrDefault(c => c.Client == message.Client);
        if (callback != null)
        {
            return false;
        }
        callback = new Callback<T>(message);
        _callbacks.Add(callback);
        Trace.TraceInformation($"{nameof(Callbacks<T>)}: Client {callback.GetHashCode()} added");
        return true;
    }

    public void Invoke(Func<T, Task> call) => InvokeAsync(call).TraceError();

    public Task InvokeAsync(Func<T, Task> call) =>
    Task.WhenAll(_callbacks.ToArray().Select(wrapper => wrapper.InvokeAsync(async callback =>
    {
        try
        {
            await call(callback);
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is IOException)
        {
            Trace.TraceInformation($"{nameof(Callbacks<T>)}: Client {callback.GetHashCode()} exited: {ex.GetType()}");
            _callbacks.Remove(wrapper);
        }
    })));
}

internal sealed class Callback<T> where T : class
{
    private readonly T _callback;
    private readonly AsyncLock _lock = new AsyncLock();

    public IClient Client { get; }

    public Callback(Message message)
    {
        Client = message.Client;
        _callback = message.Client.GetCallback<T>();
    }

    public void Invoke(Func<T, Task> call) => InvokeAsync(call).TraceError();

    public async Task InvokeAsync(Func<T, Task> call)
    {
        using (await _lock.LockAsync())
        {
            await call(_callback);
        }
    }

    public async Task FlushAsync() => (await _lock.LockAsync()).Dispose();
}

internal static class TaskExtensions
{
    public static void TraceError(this Task task, [CallerFilePath] string file = null!, [CallerMemberName] string member = null!, [CallerLineNumber] int line = default, string customMessage = default!) =>

    task.ContinueWith(result => result.Exception?.Trace($"{nameof(TraceError)}: {file}:{member}:{line} {customMessage}\n"), TaskContinuationOptions.NotOnRanToCompletion);
    public static string Trace(this Exception exception, string? label = null)
    {
        var content = exception.CreateTraceMessage(label);
        System.Diagnostics.Trace.TraceError(content);
        return content;
    }
    public static string CreateTraceMessage(this Exception exception, string? label = null)
    {
        var prefix = string.IsNullOrWhiteSpace(label) ? string.Empty : $"{label}: ";
        return $"{prefix}{ExceptionToString()}, HResult {exception.HResult}";

        string ExceptionToString()
        {
            try
            {
                return exception.ToString();
            }
            catch (Exception toStringException)
            {
                return $"{exception.GetType()}: {exception.Message} ---> ToString() of this exception failed:{Environment.NewLine}{toStringException}";
            }
        }
    }

}