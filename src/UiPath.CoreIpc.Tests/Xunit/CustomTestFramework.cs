﻿using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework(typeName: "UiPath.Ipc.Tests.CustomTestFramework", assemblyName: "UiPath.Ipc.Tests")]

namespace UiPath.Ipc.Tests;

public readonly struct CustomTestContext
{
    public required IMethodInfo Method { get; init; }
}

public class CustomTestFramework : XunitTestFramework
{
    private static readonly AsyncLocal<CustomTestContext?> AsyncLocalContext = new();
    public static CustomTestContext? Context => AsyncLocalContext.Value;

    public CustomTestFramework(IMessageSink messageSink)
        : base(messageSink)
    {
        messageSink.OnMessage(new DiagnosticMessage($"Using {nameof(CustomTestFramework)}"));
    }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
    => new CustomExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private class CustomExecutor : XunitTestFrameworkExecutor
    {
        public CustomExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
        {
        }

        protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using var assemblyRunner = new CustomAssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await assemblyRunner.RunAsync();
        }
    }

    private class CustomAssemblyRunner : XunitTestAssemblyRunner
    {
        public CustomAssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
        {
        }

        protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus, ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, CancellationTokenSource cancellationTokenSource)
            => new CustomTestCollectionRunner(testCollection, testCases, DiagnosticMessageSink, messageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), cancellationTokenSource).RunAsync();
    }

    private class CustomTestCollectionRunner : XunitTestCollectionRunner
    {
        public CustomTestCollectionRunner(ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ITestCaseOrderer testCaseOrderer, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
        : base(testCollection, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator, cancellationTokenSource)
        {
        }

        protected override Task<RunSummary> RunTestClassAsync(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases)
        => new CustomTestClassRunner(testClass, @class, testCases, DiagnosticMessageSink, MessageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), CancellationTokenSource, CollectionFixtureMappings).RunAsync();
    }

    private class CustomTestClassRunner : XunitTestClassRunner
    {
        public CustomTestClassRunner(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ITestCaseOrderer testCaseOrderer, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, IDictionary<Type, object> collectionFixtureMappings)
        : base(testClass, @class, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator, cancellationTokenSource, collectionFixtureMappings)
        {
        }

        protected override Task<RunSummary> RunTestMethodAsync(ITestMethod testMethod, IReflectionMethodInfo method, IEnumerable<IXunitTestCase> testCases, object[] constructorArguments)
        => new CustomTestMethodRunner(testMethod, Class, method, testCases, DiagnosticMessageSink, MessageBus, new ExceptionAggregator(Aggregator), CancellationTokenSource, constructorArguments).RunAsync();
    }

    private class CustomTestMethodRunner : XunitTestMethodRunner
    {
        private readonly IMessageSink _diagnosticMessageSink;

        public CustomTestMethodRunner(ITestMethod testMethod, IReflectionTypeInfo @class, IReflectionMethodInfo method, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, object[] constructorArguments)
        : base(testMethod, @class, method, testCases, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource, constructorArguments)
        {
            _diagnosticMessageSink = diagnosticMessageSink;
        }

        protected override async Task<RunSummary> RunTestCaseAsync(IXunitTestCase testCase)
        {
            var parameters = string.Empty;

            if (testCase.TestMethodArguments != null)
            {
                parameters = string.Join(", ", testCase.TestMethodArguments.Select(a => a?.ToString() ?? "null"));
            }

            var test = $"{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}({parameters})";

            _diagnosticMessageSink.OnMessage(new DiagnosticMessage($"STARTED: {test}"));

            try
            {
                AsyncLocalContext.Value = new()
                {
                    Method = testCase.Method
                };
                var result = await base.RunTestCaseAsync(testCase);

                var status = result.Failed > 0
                    ? "FAILURE"
                    : (result.Skipped > 0 ? "SKIPPED" : "SUCCESS");

                _diagnosticMessageSink.OnMessage(new DiagnosticMessage($"{status}: {test} ({result.Time}s)"));

                return result;
            }
            catch (Exception ex)
            {
                _diagnosticMessageSink.OnMessage(new DiagnosticMessage($"ERROR: {test} ({ex.Message})"));
                throw;
            }
        }
    }
}