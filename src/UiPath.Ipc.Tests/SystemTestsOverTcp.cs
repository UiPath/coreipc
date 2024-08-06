﻿using System.Net;
using UiPath.Ipc.Transport.Tcp;
using Xunit.Abstractions;

namespace UiPath.Ipc.Tests;

public sealed class SystemTestsOverTcp : SystemTests
{
    private readonly IPEndPoint _endPoint = NetworkHelper.FindFreeLocalPort();

    public SystemTestsOverTcp(ITestOutputHelper outputHelper) : base(outputHelper) { }

    protected sealed override ListenerConfig CreateListener()
    => new TcpListener()
    {
        EndPoint = _endPoint,
    };

    protected override ClientBase CreateClient()
    => new TcpClient()
    {
        EndPoint = _endPoint,
    };
}
