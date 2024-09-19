using Nito.AsyncEx;
using Nito.Disposables;

namespace UiPath.Ipc.Tests;

public class StudioOperations : IStudioOperations
{
    private readonly Callbacks<IStudioEvents> _studio = new Callbacks<IStudioEvents>();

    private readonly AsyncLock _lock = new();
    private readonly RobotInfo _latestInfo = new() { Offline = false };

    public async Task<RobotInfo> GetRobotInfoCore(StudioAgentMessage message, CancellationToken ct = default)
    {
        using (await _lock.LockAsync())
        {
            _studio.TryRegister(message, out var callback);
            return _latestInfo;
        }
    }

    public async Task<bool> SetOffline(bool value)
    {
        using (await _lock.LockAsync())
        {
            _latestInfo.Offline = value;
            await _studio.InvokeAsync(async callback => await callback.OnRobotInfoChanged(new RobotInfoChangedArgs { LatestInfo = _latestInfo }));
        }

        return true;
    }
}

public class StudioEvents : IStudioEvents
{
    private readonly object _lock = new();
    private IStudioEvents? _target = null;

    public IDisposable RouteTo(IStudioEvents? target)
    {
        lock (_lock)
        {
            var previousTarget = _target;
            _target = target;
            return new Disposable(() =>
            {
                lock (_lock)
                {
                    _target = previousTarget;
                }
            });
        }
    }

    private IStudioEvents? GetTarget()
    {
        lock (_lock)
        {
            return _target;
        }
    }

    public async Task OnRobotInfoChanged(RobotInfoChangedArgs args)
    {
        if (GetTarget() is not { } target)
        {
            return;
        }

        await target.OnRobotInfoChanged(args);
    }
}