namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed class ExceptionInfo
    {
        public static implicit operator ExceptionInfo?(Exception? ex)
        => ex is null ? null : new()
        {
            TypeName = ex.GetType().AssemblyQualifiedName!,
            Message = ex.Message,
            StackTrace = ex.StackTrace,
            InnerException = ex.InnerException,
        };

        public required string TypeName { get; init; }
        public required string Message { get; init; }
        public string? StackTrace { get; init; }
        public ExceptionInfo? InnerException { get; init; }
    }
}
