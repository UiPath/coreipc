using AutoFixture;
using UiPath.Ipc.Http;
using UiPath.Ipc.Transport.NamedPipe;
using UiPath.Ipc.Transport.Tcp;
using UiPath.Ipc.Transport.WebSocket;

namespace UiPath.Ipc.Tests;

public class ListenerConfigTests
{
    private static readonly TimeSpan NewRequestTimeout = TimeSpan.FromDays(365);
    private static readonly byte NewMaxReceivedMessageSizeInMegabytes = 111;

    [Theory]
    [MemberData(nameof(TestBaseConfigureCoreShouldWorkCases))]
    public void TestBaseConfigureCoreShouldWork(ListenerConfigCase @case)
    {
        var modified = TestBase.ConfigureCore(@case.ListenerConfig, NewRequestTimeout, NewMaxReceivedMessageSizeInMegabytes);

        @case.Validate(modified);
        modified.RequestTimeout.ShouldBe(NewRequestTimeout);
        modified.MaxReceivedMessageSizeInMegabytes.ShouldBe(NewMaxReceivedMessageSizeInMegabytes);
    }

    public static IEnumerable<object?[]> TestBaseConfigureCoreShouldWorkCases()
    {
        Fixture fixture = new();

        var namedPipeListener = fixture.Create<NamedPipeListener>();
        var tcpListener = fixture.Create<TcpListener>();
        var webSocketListener = fixture.Create<WebSocketListener>();
        var bidiHttpListener = fixture.Create<BidiHttpListener>();

        yield return Make(namedPipeListener, actual => ValidateNamedPipeListener(actual, namedPipeListener));
        yield return Make(tcpListener, actual => ValidateTcpListener(actual, tcpListener));
        yield return Make(webSocketListener, actual => ValidateWebSocketListener(actual, webSocketListener));
        yield return Make(bidiHttpListener, actual => ValidateBidiHttpListener(actual, bidiHttpListener));

        static void ValidateNamedPipeListener(NamedPipeListener actual, NamedPipeListener expected)
        {
            actual.PipeName.ShouldBe(expected.PipeName);
            actual.ServerName.ShouldBe(expected.ServerName);
            actual.AccessControl.ShouldBe(expected.AccessControl);

            ValidateListenerConfig(actual, expected);
        }
        static void ValidateTcpListener(TcpListener actual, TcpListener expected)
        {
            actual.EndPoint.ShouldBe(expected.EndPoint);

            ValidateListenerConfig(actual, expected);
        }
        static void ValidateWebSocketListener(WebSocketListener actual, WebSocketListener expected)
        {
            actual.Accept.ShouldBeSameAs(expected.Accept);

            ValidateListenerConfig(actual, expected);
        }
        static void ValidateBidiHttpListener(BidiHttpListener actual, BidiHttpListener expected)
        {
            actual.Uri.ShouldBe(expected.Uri);

            ValidateListenerConfig(actual, expected);
        }

        static void ValidateListenerConfig(ListenerConfig actual, ListenerConfig expected)
        {
            actual.ConcurrentAccepts.ShouldBe(expected.ConcurrentAccepts);
            actual.MaxReceivedMessageSizeInMegabytes.ShouldBe(expected.MaxReceivedMessageSizeInMegabytes);
            actual.Certificate.ShouldBe(expected.Certificate);

            ValidateEndpointConfig(actual, expected);
        }
        static void ValidateEndpointConfig(EndpointConfig actual, EndpointConfig expected)
        {
            actual.RequestTimeout.ShouldBe(expected.RequestTimeout);
        }

        object?[] Make<T>(T config, Action<T> validate) where T : ListenerConfig
        {
            return [new ListenerConfigCase(config, x => validate(x as T ?? throw new InvalidOperationException()))];
        }
    }

    public sealed record ListenerConfigCase(ListenerConfig ListenerConfig, Action<ListenerConfig> Validate);
}
