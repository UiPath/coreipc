using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public class SyncOverAsyncTests
{
    [Theory]
    [InlineData(ScenarioId.Inline)]
    [InlineData(ScenarioId.GuiLikeSynchronizationContext)]
    [InlineData(ScenarioId.GuiLikeTaskScheduler)]
    [InlineData(ScenarioId.ThreadPoolTaskScheduler
)]
    public async Task RemoteCallingSyncOverAsync_IpcShouldBeResilient(ScenarioId scenarioId)
    {
        var pipeName = $"{Guid.NewGuid():N}";

        await using var ipcServer = CreateServer(pipeName);
        await ipcServer.WaitForStart();

        var ipcClient = CreateClient(pipeName);

        var proxy = ipcClient.GetProxy<IComputingService>();

        var tcsDone = new TaskCompletionSource<object?>();

        var scenario = scenarioId.CreateScenario();
        scenario.Run(() =>
        {
            try
            {
                var result = proxy.AddFloats(2, 3).Result;//.GetAwaiter().GetResult();
                tcsDone.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcsDone.SetCanceled();
            }
            catch (Exception ex)
            {
                tcsDone.SetException(ex);
            }
        });

        await tcsDone.Task.ShouldBeAsync(5).ShouldCompleteInAsync(TimeSpan.FromSeconds(20));
    }

    private static IpcServer CreateServer(string pipeName)
    => new IpcServer
    {
        Listeners = [
            new NamedPipeListener
                {
                    PipeName = pipeName,
                }
        ],
        Endpoints = new()
        {
            typeof(IComputingService)
        },
        ServiceProvider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IComputingService, ComputingService>()
            .BuildServiceProvider()
    };

    private static IpcClient CreateClient(string pipeName)
    => new()
    {
        Transport = new NamedPipeTransport { PipeName = pipeName },
        Config = new()
    };



    public enum ScenarioId
    {
        Inline,
        GuiLikeSynchronizationContext,
        GuiLikeTaskScheduler,
        ThreadPoolTaskScheduler
    }

    public abstract class Scenario
    {
        public abstract void Run(Action action);

        public sealed class Inline : Scenario
        {
            public override void Run(Action action) => action();
        }

        public abstract class SynchronizationContextScenario : Scenario
        {
            private readonly Lazy<SynchronizationContext> _synchronizationContext;

            public SynchronizationContextScenario() => _synchronizationContext = new(CreateSynchronizationContext);

            protected abstract SynchronizationContext CreateSynchronizationContext();

            public override void Run(Action action)
            {
                _synchronizationContext.Value.Post(_ => action(), state: null);
            }
        }

        public sealed class GuiLikeSynchronizationContext : SynchronizationContextScenario
        {
            protected override SynchronizationContext CreateSynchronizationContext()
            => new Nito.AsyncEx.AsyncContextThread().Context.SynchronizationContext;
        }

        public abstract class TaskSchedulerScenario : Scenario
        {
            private readonly Lazy<TaskScheduler> _taskScheduler;

            public TaskSchedulerScenario() => _taskScheduler = new(CreateTaskScheduler);

            public override void Run(Action action)
            {
                _ = Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, _taskScheduler.Value);
            }

            protected abstract TaskScheduler CreateTaskScheduler();
        }

        public sealed class GuiLikeTaskScheduler : TaskSchedulerScenario
        {
            protected override TaskScheduler CreateTaskScheduler() => new ConcurrentExclusiveSchedulerPair().ConcurrentScheduler;
        }
        public sealed class ThreadPoolTaskScheduler : TaskSchedulerScenario
        {
            protected override TaskScheduler CreateTaskScheduler() => TaskScheduler.Default;
        }
    }
}

internal static class SyncOverAsyncTests_ScenarioIdExtensions
{
    public static SyncOverAsyncTests.Scenario CreateScenario(this SyncOverAsyncTests.ScenarioId id) => id switch
    {
        SyncOverAsyncTests.ScenarioId.Inline => new SyncOverAsyncTests.Scenario.Inline(),
        SyncOverAsyncTests.ScenarioId.GuiLikeSynchronizationContext => new SyncOverAsyncTests.Scenario.GuiLikeSynchronizationContext(),
        SyncOverAsyncTests.ScenarioId.GuiLikeTaskScheduler => new SyncOverAsyncTests.Scenario.GuiLikeTaskScheduler(),
        SyncOverAsyncTests.ScenarioId.ThreadPoolTaskScheduler => new SyncOverAsyncTests.Scenario.ThreadPoolTaskScheduler(),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}