using BenchmarkDotNet.Running;
using UiPath.Ipc.Benchmarks;


// await Run(TechnologyId.New);
// BenchmarkRunner.Run<IpcBenchmark>();
BenchmarkRunner.Run<SchedulerBenchmark>();

static async Task Run(TechnologyId techId)
{
    await using var tech = techId.Create();
    await tech.Init();
    var x = await tech.GetProxy().AddFloats(1, 1);
    Console.WriteLine($"{nameof(x)} == {x}");
}