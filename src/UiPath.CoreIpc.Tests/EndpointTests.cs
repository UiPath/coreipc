﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UiPath.CoreIpc.NamedPipe;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Shouldly;

namespace UiPath.CoreIpc.Tests
{
    public class EndpointTests : IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);
        private readonly ServiceHost _host;
        private readonly IComputingService _computingClient;
        private readonly ISystemService _systemClient;
        private readonly ComputingService _computingService;
        private readonly SystemService _systemService;
        private readonly ComputingCallback _computingCallback;
        private readonly SystemCallback _systemCallback;
        private readonly IServiceProvider _serviceProvider;
        public EndpointTests()
        {
            _computingCallback = new ComputingCallback { Id = Guid.NewGuid().ToString() };
            _systemCallback = new SystemCallback { Id = Guid.NewGuid().ToString() };
            _serviceProvider = IpcHelpers.ConfigureServices();
            _computingService = (ComputingService)_serviceProvider.GetService<IComputingService>();
            _systemService = (SystemService)_serviceProvider.GetService<ISystemService>();
            _host = new ServiceHostBuilder(_serviceProvider)
                .UseNamedPipes(new NamedPipeSettings("EndpointTests") { RequestTimeout = RequestTimeout })
                .AddEndpoint<IComputingService, IComputingCallback>()
                .AddEndpoint<ISystemService, ISystemCallback>()
                .Build();
            _host.RunAsync();
            _computingClient = ComputingClientBuilder().Build();
            _systemClient = CreateSystemService();
        }
        private NamedPipeClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null) =>
            new NamedPipeClientBuilder<IComputingService, IComputingCallback>("EndpointTests", _serviceProvider)
                .AllowImpersonation()
                .RequestTimeout(RequestTimeout)
                .CallbackInstance(_computingCallback)
                .TaskScheduler(taskScheduler);
        private ISystemService CreateSystemService() => SystemClientBuilder().Build();
        private NamedPipeClientBuilder<ISystemService, ISystemCallback> SystemClientBuilder() =>
            new NamedPipeClientBuilder<ISystemService, ISystemCallback>("EndpointTests", _serviceProvider).CallbackInstance(_systemCallback).RequestTimeout(RequestTimeout).AllowImpersonation();
        public void Dispose()
        {
            ((IDisposable)_computingClient).Dispose();
            ((IDisposable)_systemClient).Dispose();
            _host.Dispose();
            ((InterceptorProxy)_computingClient).CloseConnection();
            ((InterceptorProxy)_systemClient).CloseConnection();
        }
        [Fact]
        public Task CallbackConcurrently() => Task.WhenAll(Enumerable.Range(1, 50).Select(_ => Callback()));
        [Fact]
        public async Task Callback()
        {
            var message = new SystemMessage { Text = Guid.NewGuid().ToString() };
            var computingTask = _computingClient.SendMessage(message);
            var systemTask = _systemClient.SendMessage(message);
            await Task.WhenAll(computingTask, systemTask);
            systemTask.Result.ShouldBe($"{Environment.UserName}_{_systemCallback.Id}_{message.Text}");
            computingTask.Result.ShouldBe($"{Environment.UserName}_{_computingCallback.Id}_{message.Text}");
        }
    }
}