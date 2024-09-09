namespace UiPath.Ipc.TV;

public interface IProjectContext : IAsyncDisposable
{
    Func<ValueTask> DisposeScope { get; }
    string ProjectPath { get; }
}

public class ProjectContext : IProjectContext
{
    public required string ProjectPath { get; set; }
    public required Func<ValueTask> DisposeScope { get; set; }

    private readonly Lazy<Task> _dispose;

    public ProjectContext()
    {
        _dispose = new(() => DisposeScope!().AsTask());
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(_dispose.Value);
}