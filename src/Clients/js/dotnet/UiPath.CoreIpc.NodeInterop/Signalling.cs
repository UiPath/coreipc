using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace UiPath.Ipc.NodeInterop;

internal static class Signalling
{

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SignalKind
    {
        Throw,
        PoweringOn,
        ReadyToConnect,
    }

    public class Signal
    {
        public static implicit operator Signal(SignalKind signalKind) => new Signal { Kind = signalKind };
        public SignalKind Kind { get; set; }
    }

    public class Signal<TDetails> : Signal
    {
        public required TDetails Details { get; init; }
    }

    public static Signal<TDetails> MakeSignal<TDetails>(SignalKind kind, TDetails details) => new Signal<TDetails>
    {
        Kind = kind,
        Details = details
    };

    public static void Send(Signal signal) => Console.WriteLine($"###{JsonConvert.SerializeObject(signal)}");

    public static void Throw(Exception exception)
        => Send(MakeSignal(SignalKind.Throw, new
        {
            Type = exception.GetType().Name,
            Message = exception.Message
        }));

    public static void CannotConnect(Exception exception)
        => Send(MakeSignal(SignalKind.ReadyToConnect, new
        {
            Type = exception.GetType().Name,
            Message = exception.Message            
        }));
}
