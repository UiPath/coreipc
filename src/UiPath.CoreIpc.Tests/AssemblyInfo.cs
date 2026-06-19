// Run the whole suite serialized (no cross-collection parallelism). Several
// tests touch process-wide state — e.g. the System.Diagnostics.Trace listener
// in NamedPipeSmokeTests.Teardown, and shared named-pipe/port resources — which
// is unsafe under xUnit's default parallel execution.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
