using Newtonsoft.Json;

namespace UiPath.Ipc;

partial class Telemetry
{
    public interface IOperationStart
    {
        Id Id { get; }
    }

    public interface IVoidOperation
    {
        VoidSucceeded CreateSucceeded();
        VoidFailed CreateFailed(Exception? ex);
    }

    public interface INonVoidOperation
    {
        ResultSucceeded CreateSucceeded(object? result);
        VoidFailed CreateFailed(Exception? ex);
    }

    public interface ILoggable
    {
        ILogger? Logger { get; }
        string LogMessage { get; }
        LogLevel LogLevel { get; }
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
        Id? Of { get; }
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
            record.CreateSucceeded().Log();
        }
        catch (Exception ex)
        {
            record.CreateFailed(ex).Log();
            throw;
        }
    }
    public static async Task Monitor<TOperationStart>(this TOperationStart record, Func<Task> asyncAction) where TOperationStart : Telemetry.RecordBase
    {
        Telemetry.Log(record);

        try
        {
            await asyncAction();
            record.CreateSucceeded().Log();
        }
        catch (Exception ex)
        {
            record.CreateFailed(ex).Log();
            throw;
        }
    }

    public static Task<TResult> Monitor<TResult>(
        this Telemetry.RecordBase record,
        Func<Task<TResult>> asyncFunc)
    => record.Monitor(
        (result, record) => record.Succeed(result),
        asyncFunc);

    public static async Task<TResult> Monitor<TResult, TRecord>(
        this TRecord record,
        Func<TResult, TRecord, Telemetry.RecordBase>? sanitizeSucceeded,
        Func<Task<TResult>> asyncFunc)
        where TRecord : Telemetry.RecordBase
    {
        Telemetry.Log(record);

        try
        {
            var result = await asyncFunc();
            var succeeded = sanitizeSucceeded?.Invoke(result, record) ?? record.Succeed(result);
            succeeded.Log();
            return result;
        }
        catch (Exception ex)
        {
            var recordFailed = record switch
            {
                Telemetry.INonVoidOperation nvo => nvo.CreateFailed(ex),
                _ => new Telemetry.VoidFailed { StartId = record.Id, Exception = ex }
            };
            recordFailed.Log();
            
            throw;
        }
    }

    public static Telemetry.ResultSucceeded Succeed(this Telemetry.RecordBase record, object? result)
    {
        if (record is Telemetry.INonVoidOperation nvo)
        {
            return nvo.CreateSucceeded(result);
        }

        string json;
        try
        {
            json = JsonConvert.SerializeObject(result, Telemetry.Jss);
        }
        catch (Exception ex)
        {
            json = JsonConvert.SerializeObject(new
            {
                Exception = ex.ToString(),
                ResultType = result?.GetType().AssemblyQualifiedName,
            }, Formatting.Indented);
        }
        return new Telemetry.ResultSucceeded { StartId = record.Id, ResultJson = json };
    }
}
