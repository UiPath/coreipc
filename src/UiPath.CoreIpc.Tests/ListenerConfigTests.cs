using AutoFixture;
using AutoFixture.Kernel;
using System.Net;
using System.Net.WebSockets;
using UiPath.Ipc.Http;
using UiPath.Ipc.Transport.NamedPipe;
using UiPath.Ipc.Transport.Tcp;
using UiPath.Ipc.Transport.WebSocket;

namespace UiPath.Ipc.Tests;

public class ListenerConfigTests
{
    private static readonly TimeSpan OldRequestTimeout = TimeSpan.FromSeconds(1);
    private static readonly byte OldMaxReceivedMessageSizeInMegabytes = 100;

    private static readonly TimeSpan NewRequestTimeout = OldRequestTimeout + TimeSpan.FromSeconds(10);
    private static readonly byte NewMaxReceivedMessageSizeInMegabytes = (byte)(OldMaxReceivedMessageSizeInMegabytes + 10);

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
        var fixture = new Fixture();

        var namedPipeListener = new NamedPipeListener()
        {
            PipeName = fixture.Create<string>(),
            ServerName = fixture.Create<string>(),
            AccessControl = pipeSecurity => { },
            ConcurrentAccepts = fixture.Create<int>(),
            Certificate = null,
            MaxReceivedMessageSizeInMegabytes = OldMaxReceivedMessageSizeInMegabytes,
            RequestTimeout = OldRequestTimeout
        };

        var tcpListener = new TcpListener()
        {
            EndPoint = fixture.Create<IPEndPoint>(),
            ConcurrentAccepts = fixture.Create<int>(),
            Certificate = null,
            MaxReceivedMessageSizeInMegabytes = OldMaxReceivedMessageSizeInMegabytes,
            RequestTimeout = OldRequestTimeout
        };

        var webSocketListener = new WebSocketListener()
        {
            Accept = async ct => null
        };

        var bidiHttpListener = new BidiHttpListener()
        {
            Uri = fixture.Create<Uri>(),
            ConcurrentAccepts = fixture.Create<int>(),
            Certificate = null,
            MaxReceivedMessageSizeInMegabytes = OldMaxReceivedMessageSizeInMegabytes,
            RequestTimeout = OldRequestTimeout
        };

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
            actual.Certificate.ShouldBe(expected.Certificate);

            ValidateEndpointConfig(actual, expected);
        }
        static void ValidateEndpointConfig(EndpointConfig actual, EndpointConfig expected)
        {
        }

        object?[] Make<T>(T config, Action<T> validate) where T : ListenerConfig
        {
            return [new ListenerConfigCase(config, x => validate(x as T ?? throw new InvalidOperationException()))];
        }
    }

    public sealed record ListenerConfigCase(ListenerConfig ListenerConfig, Action<ListenerConfig> Validate);

    private sealed class MockStream : Stream
    {
        public override bool CanTimeout => true;
        public override int ReadTimeout { get; set; }
        public override int WriteTimeout { get; set; }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => throw new NotImplementedException();
        public override long Length => 0;
        public override long Position { get; set; }
    }
}
