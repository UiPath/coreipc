namespace UiPath.Ipc.Tests;

internal sealed class TraceErrorCapture : TraceListener
{
    private readonly List<string> _errors = new();

    public TraceErrorCapture() => Trace.Listeners.Add(this);

    public string[] Errors
    {
        get { lock (_errors) return _errors.ToArray(); }
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (eventType == TraceEventType.Error && message is not null)
        {
            lock (_errors) _errors.Add(message);
        }
    }

    public override void Write(string? message) { }
    public override void WriteLine(string? message) { }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Trace.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}
