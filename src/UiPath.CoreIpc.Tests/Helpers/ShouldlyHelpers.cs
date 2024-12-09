using System.Runtime.CompilerServices;

namespace UiPath.CoreIpc.Tests;

[ShouldlyMethods]
internal static class ShouldlyHelpers
{
    public static async Task ShouldBeAsync<T>(this Task<T> task, T expected, [CallerArgumentExpression(nameof(task))] string? taskExpression = null, bool launchDebugger = false)
    {
        var actual = await task;
        try
        {
            actual.ShouldBe(expected);
        }
        catch (Exception ex)
        {
            if (launchDebugger)
            {
                Debugger.Launch();
            }
            throw new ShouldAssertException($"Awaiting the expression `{taskExpression}`\r\n\tshould yield\r\n{expected}\r\n\tbut actually yielded\r\n{actual}", ex);
        }
    }

    public static async Task<T> ShouldNotBeNullAsync<T>(this Task<T> task, [CallerArgumentExpression(nameof(task))] string? taskExpression = null)
        where T : class
    {
        var actual = await task;
        try
        {
            return actual.ShouldNotBeNull();
        }
        catch
        {
            throw new ShouldAssertException($"The provided expression `{taskExpression}`\r\n\tshouldn't have yielded null but did.");
        }
    }


    public static async Task<T> ShouldNotThrowAsyncAnd<T>(this Task<T> task, [CallerArgumentExpression(nameof(task))] string? taskExpression = null)
    {
        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            throw new ShouldAssertException($"The provided expression `{taskExpression}`\r\n\tshould not throw but threw\r\n{ex.GetType().FullName}\r\n\twith message\r\n\"{ex.Message}\"", ex);
        }
    }


    public static Task ShouldCompleteInAsync(this Task task, TimeSpan lease, [CallerArgumentExpression(nameof(task))] string? taskExpression = null)
    => task.Return(0).ShouldCompleteInAsync(lease, taskExpression);

    public static async Task<T> ShouldCompleteInAsync<T>(this Task<T> task, TimeSpan lease, [CallerArgumentExpression(nameof(task))] string? taskExpression = null)
    {
        using var cts = new CancellationTokenSource(lease);
        try
        {
            return await Nito.AsyncEx.TaskExtensions.WaitAsync(task, cts.Token);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            throw new ShouldCompleteInException($"The task {taskExpression} should complete in {lease} but did not.", inner: null);
        }
    }

    public static async Task ShouldStallForAtLeastAsync<T>(this Task<T> task, TimeSpan lease, [CallerArgumentExpression(nameof(task))] string? taskExpression = null)
    {
        using var cts = new CancellationTokenSource(lease);
        try
        {
            _ = await Nito.AsyncEx.TaskExtensions.WaitAsync(task, cts.Token);
            throw new ShouldAssertException($"The task {taskExpression} should stall for at least {lease} but it completed faster.");
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {            
        }
    }


    public static async Task ShouldSatisfyAllConditionsAsync<T>(this Task<T> task, Action<T>[] assertions, [CallerArgumentExpression(nameof(task))] string? taskExpression = null, [CallerArgumentExpression(nameof(assertions))] string? assertionsExpression = null)
    {
        var actual = await task;
        var exceptions = new List<Exception>();
        foreach (var assertion in assertions)
        {
            try
            {
                assertion(actual);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }


        var innerException = exceptions switch
        {
            [] => null,
            [var singleException] => singleException,
            _ => new AggregateException(exceptions)
        };

        if (innerException is not null)
        {
            throw new ShouldAssertException($"Awaiting the expression `{taskExpression}`\r\n\tshould yield a value that satisfies these assertions\r\n{assertionsExpression}\r\n\tbut at least one assertion was not satisfied.", innerException);
        }
    }
    private static async Task<T> Return<T>(this Task task, T value = default!)
    {
        await task;
        return value;
    }
}
