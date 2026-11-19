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

    [Theory]
    [InlineData(LogLevel.Critical, TraceEventType.Error)]
    [InlineData(LogLevel.Error, TraceEventType.Error)]
    [InlineData(LogLevel.Warning, TraceEventType.Warning)]
    [InlineData(LogLevel.Information, TraceEventType.Information)]
    [InlineData(LogLevel.Debug, TraceEventType.Information)]
    [InlineData(LogLevel.Trace, TraceEventType.Information)]
    public void WithoutALogger_TheFailureIsTraced_BelowWarningAsInformation(LogLevel logLevel, TraceEventType expectedTraceEventType)
    {
        using var traces = new TraceCapture();
        var failure = new InvalidOperationException(Guid.NewGuid().ToString());

        default(ILogger).LogException(failure, nameof(FailedCallLogLevelTests), logLevel);

        traces.EventTypesOf(failure.Message).ShouldBe([expectedTraceEventType]);
    }

    [Fact]
    public void WithoutALogger_None_IsNotTraced()
    {
        using var traces = new TraceCapture();
        var failure = new InvalidOperationException(Guid.NewGuid().ToString());

        default(ILogger).LogException(failure, nameof(FailedCallLogLevelTests), LogLevel.None);

        traces.EventTypesOf(failure.Message).ShouldBeEmpty();
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

    // Trace listeners are process-wide, so a test picks its own events out by content.
    private sealed class TraceCapture : TraceListener
    {
        private readonly List<(TraceEventType EventType, string Message)> _events = new();

        public TraceCapture() => Trace.Listeners.Add(this);

        public TraceEventType[] EventTypesOf(string messagePart)
        {
            lock (_events)
            {
                return _events.Where(traced => traced.Message.Contains(messagePart)).Select(traced => traced.EventType).ToArray();
            }
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (message is not null)
            {
                lock (_events)
                {
                    _events.Add((eventType, message));
                }
            }
        }

        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Trace.Listeners.Remove(this);
            }
            base.Dispose(disposing);
        }
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
