namespace UiPath.Ipc;

internal class FullStackTraceException : Exception
{
    private readonly string _stackTrace;

    public FullStackTraceException(string? message = null, Exception? innerException = null)
        : base(message, innerException)
    {
        _stackTrace = new StackTrace(fNeedFileInfo: true).ToString();
    }

    public override string? StackTrace => _stackTrace;
}

