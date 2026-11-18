using Microsoft.Extensions.Logging;

namespace UiPath.Ipc.Tests;

public interface IFailingCalls
{
    Task<bool> Fail(LogLevel? failedCallLogLevel);
    Task<bool> CallFailingCallback();
}

public interface IFailingCallback
{
    Task<bool> Fail(LogLevel failedCallLogLevel);
}

public sealed class FailingCalls : IFailingCalls
{
    public const string Message = "call failure";

    public Task<bool> Fail(LogLevel? failedCallLogLevel)
    {
        if (failedCallLogLevel is { } logLevel)
        {
            IpcContext.Current!.FailedCallLogLevel = logLevel;
        }
        throw new InvalidOperationException(Message);
    }

    public async Task<bool> CallFailingCallback()
    {
        await IpcContext.Current!.GetCallback<IFailingCallback>().Fail(LogLevel.Warning).ShouldThrowAsync<RemoteException>();
        return true;
    }
}

public sealed class FailingCallback : IFailingCallback
{
    public const string Message = "callback failure";

    public Task<bool> Fail(LogLevel failedCallLogLevel)
    {
        IpcContext.Current!.FailedCallLogLevel = failedCallLogLevel;
        throw new InvalidOperationException(Message);
    }
}

public sealed class FailedCallLogLevelTests
{
    private readonly LogCapture _serverLogs = new();

    [Fact]
    public async Task NotSet_FailedCallIsLoggedAsError()
    {
        await using var pair = await CreatePair();

        await pair.Proxy.Fail(failedCallLogLevel: null).ShouldThrowAsync<RemoteException>();

        _serverLogs.LevelsOf(FailingCalls.Message).ShouldBe([LogLevel.Error]);
    }

    [Fact]
    public async Task SetByTheHandler_FailedCallIsLoggedAtThatLevel_TheNextCallIsNotAffected()
    {
        await using var pair = await CreatePair();

        var exception = await pair.Proxy.Fail(LogLevel.Debug).ShouldThrowAsync<RemoteException>();
        await pair.Proxy.Fail(failedCallLogLevel: null).ShouldThrowAsync<RemoteException>();

        exception.Message.ShouldBe(FailingCalls.Message);
        _serverLogs.LevelsOf(FailingCalls.Message).ShouldBe([LogLevel.Debug, LogLevel.Error]);
    }

    [Fact]
    public async Task None_FailedCallIsNotLogged_TheCallerStillGetsTheError()
    {
        await using var pair = await CreatePair();

        var exception = await pair.Proxy.Fail(LogLevel.None).ShouldThrowAsync<RemoteException>();

        exception.Message.ShouldBe(FailingCalls.Message);
        _serverLogs.LevelsOf(FailingCalls.Message).ShouldBeEmpty();
    }

    [Fact]
    public async Task Callback_TheLevelItsHandlerSetApplies()
    {
        var clientLogs = new LogCapture();
        await using var pair = await CreatePair(client =>
        {
            client.Callbacks = new() { { typeof(IFailingCallback), new FailingCallback() } };
            client.Logger = clientLogs.CreateLogger("client");
        });

        (await pair.Proxy.CallFailingCallback()).ShouldBeTrue();

        clientLogs.LevelsOf(FailingCallback.Message).ShouldBe([LogLevel.Warning]);
        _serverLogs.LevelsOf(FailingCallback.Message).ShouldBeEmpty();
    }

    private Task<IpcPair<IFailingCalls>> CreatePair(Action<IpcClient>? configureClient = null)
    {
        IFailingCalls service = new FailingCalls();
        return IpcPair<IFailingCalls>.Create(service, server =>
            server.ServiceProvider = new ServiceCollection()
                .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_serverLogs))
                .AddSingleton(service)
                .BuildServiceProvider(),
            configureClient);
    }

    private sealed class LogCapture : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public LogLevel[] LevelsOf(string messagePart)
        {
            lock (_entries)
            {
                return _entries.Where(entry => entry.Message.Contains(messagePart)).Select(entry => entry.Level).ToArray();
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(LogCapture capture) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (capture._entries)
                {
                    capture._entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
