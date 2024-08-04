using System.Net;
using System.Net.Sockets;

namespace UiPath.Ipc.Tests;

public static class NetworkHelper
{
    public static IPEndPoint FindFreeLocalPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return (IPEndPoint)socket.LocalEndPoint!;
    }

    public static void FindFreeLocalPort(out Uri ws, out Uri http)
    {
        var port = FindFreeLocalPort().Port;
        ws = new UriBuilder("ws", "localhost", port).Uri;
        http = new UriBuilder("http", "localhost", port).Uri;
    }
}
