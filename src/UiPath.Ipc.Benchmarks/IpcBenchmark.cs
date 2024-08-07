using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using UiPath.Ipc.Benchmarks;

[SimpleJob(RuntimeMoniker.Net461, baseline: true)]
[SimpleJob(RuntimeMoniker.Net80)]
[RPlotExporter]
public class IpcBenchmark
{
    private Technology _technology = null!;
    private Technology.IProxy _proxy = null!;

    [Params(TechnologyId.Old, TechnologyId.New)]
    public TechnologyId TechId;

    [GlobalSetup]
    public async Task Setup()
    {
        _technology = TechId.Create();
        await _technology.Init();
        _proxy = _technology.GetProxy();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _technology.DisposeAsync();
    }

    [Benchmark]
    public async Task SimpleCall()
    {
        _ = await _proxy.AddFloats(1, 1);
    }

    [Benchmark]
    public async Task CallWithCallback()
    {
        _ = await _proxy.GetCallbackThreadName();
    }
}