using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.Ipc.NodeInterop;

internal static class Contracts
{
    public interface IArithmetic
    {
        Task<int> Sum(int x, int y);
        Task<bool> SendMessage(Message<int> message);
    }

    public interface IAlgebra
    {
        Task<string> Ping();
        Task<int> MultiplySimple(int x, int y);
        Task<int> Multiply(int x, int y, Message message = default!);
        Task<bool> Sleep(int milliseconds, Message message = default!, CancellationToken ct = default);
        Task<bool> Timeout();
        Task<int> Echo(int x);
        Task<bool> TestMessage(Message<int> message);
    }

    public interface ICalculus
    {
        Task<string> Ping();
    }

    public interface IBrittleService
    {
        Task<int> Sum(int x, int y, TimeSpan delay, DateTime? crashBeforeUtc);

        Task Kill();
    }

    public interface IEnvironmentVariableGetter
    {
        Task<string?> Get(string variable);
    }

    /// <summary>Hosted by the client; the string is whatever the client composes
    /// from the trace context it received with the callback.</summary>
    public interface ITraceProbeCallback
    {
        Task<string> Probe();
    }

    public interface ITraceProbe
    {
        Task<TraceProbeResult> Probe();

        /// <summary>Calls the client's ITraceProbeCallback.Probe from inside the traced call,
        /// so the callback's parent span is the client span started under this server's activity.</summary>
        Task<TraceProbeCallbackResult> ProbeViaCallback();

        /// <summary>Like <see cref="ProbeViaCallback"/>, but the callback carries exactly the given
        /// values (IpcTelemetry.Context.Use), whatever the incoming call carried.</summary>
        Task<TraceProbeCallbackResult> ProbeViaCallbackWithTraceContext(string traceParent, string? traceState, string? baggage);
    }

    /// <summary>What a trace-probe handler saw: the raw values received
    /// (IpcContext.Current.TraceContext) and the Activity it ran under.</summary>
    public class TraceProbeResult
    {
        public string? TraceParent { get; set; }
        public string? TraceState { get; set; }
        public string? Baggage { get; set; }
        public string? ActivityTraceId { get; set; }
        public string? ActivityParentSpanId { get; set; }
        public string? ActivitySpanId { get; set; }
        /// <summary>Activity.Current's decoded baggage as "key=value", in wire order.</summary>
        public string[]? ActivityBaggage { get; set; }
    }

    public class TraceProbeCallbackResult
    {
        /// <summary>What ITraceProbeCallback.Probe returned.</summary>
        public string? Callback { get; set; }
        public string? ServerActivityTraceId { get; set; }
        public string? ServerActivitySpanId { get; set; }
    }

    public interface IDtoService
    {
        Task<Dto> ReturnDto(Dto dto);
    }
    public class Dto
    {
        public bool BoolProperty { get; set; }
        public int IntProperty { get; set; }
        public string StringProperty { get; set; }

        public Dto(bool boolProperty, int intProperty, string stringProperty)
        {
            BoolProperty = boolProperty;
            IntProperty = intProperty;
            StringProperty = stringProperty;
        }
    }

}
