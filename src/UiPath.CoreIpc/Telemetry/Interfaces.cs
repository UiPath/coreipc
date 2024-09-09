namespace UiPath.Ipc;

partial class Telemetry
{
    public interface IOperationStart
    {
        Telemetry.Id Id { get; }
    }

    public interface IOperationEnd
    {
        Telemetry.Id StartId { get; }
    }

    public interface IOperationFailed
    {
        Telemetry.ExceptionInfo? Exception { get; }
    }

    public interface ISubOperation
    {
        string ParentId { get; }
    }

    public interface IExternallyTriggered { }

    public interface Is<TRole> where TRole : RoleBase
    {
        Telemetry.Id? Of { get; }
    }

    public abstract class RoleBase
    {
    }
    public sealed class SubOperation : RoleBase { }
    public sealed class Effect : RoleBase { }
    public sealed class Modifier : RoleBase { }

    public sealed class Success : RoleBase { }
    public sealed class Failure : RoleBase { }
}


public static class OperationStartExtensions
{
    public static void Monitor<TOperationStart>(this TOperationStart record, Action action) where TOperationStart : Telemetry.RecordBase
    {
        Telemetry.Log(record);

        try
        {
            action();
            new Telemetry.VoidSucceeded { StartId = record.Id }.Log();
        }
        catch (Exception ex)
        {
            new Telemetry.VoidFailed { StartId = record.Id, Exception = ex }.Log();
            throw;
        }
    }
    public static async Task Monitor<TOperationStart>(this TOperationStart record, Func<Task> asyncAction) where TOperationStart : Telemetry.RecordBase
    {
        Telemetry.Log(record);

        try
        {
            await asyncAction();
            new Telemetry.VoidSucceeded { StartId = record.Id }.Log();
        }
        catch (Exception ex)
        {
            new Telemetry.VoidFailed { StartId = record.Id, Exception = ex }.Log();
            throw;
        }
    }
    public static async Task<TResult> Monitor<TResult>(this Telemetry.RecordBase record, Func<Task<TResult>> asyncFunc)
    {
        Telemetry.Log(record);

        try
        {
            var result = await asyncFunc();
            new Telemetry.ResultSucceeded { StartId = record.Id, Result = result }.Log();
            return result;
        }
        catch (Exception ex)
        {
            new Telemetry.VoidFailed { StartId = record.Id, Exception = ex }.Log();
            throw;
        }
    }
}
