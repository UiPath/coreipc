using System.Collections.Concurrent;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public abstract class SpyTestBase : TestBase
{
    protected readonly ConcurrentBag<CallInfo> _clientBeforeCalls = new();

    protected SpyTestBase(ITestOutputHelper outputHelper) : base(outputHelper)
    {
    }

    protected override void ConfigureClient(IpcClient ipcClient)
    {
        base.ConfigureClient(ipcClient);

        ipcClient.BeforeOutgoingCall = async (callInfo, _) => _clientBeforeCalls.Add(callInfo);
    }
}
