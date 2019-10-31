﻿using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public sealed class Connection : IDisposable
    {
        private readonly int _maxMessageSize;
        private readonly Lazy<Task> _receiveLoop;
        private readonly AsyncLock _sendLock = new AsyncLock();

        public Connection(Stream network, ILogger logger, string name, int maxMessageSize = int.MaxValue)
        {
            Network = network;
            Logger = logger;
            Name = $"{name} {GetHashCode()}";
            _maxMessageSize = maxMessageSize;
            _receiveLoop = new Lazy<Task>(ReceiveLoop);
        }

        public Task Listen() => _receiveLoop.Value;
        public event EventHandler<DataReceivedEventsArgs> RequestReceived;
        public event EventHandler<DataReceivedEventsArgs> ResponseReceived;
        public event EventHandler<EventArgs> Closed;
        public Task SendRequest(byte[] requestBytes, CancellationToken cancellationToken) => SendMessage(MessageType.Request, requestBytes, cancellationToken);
        public Task SendResponse(byte[] responseBytes, CancellationToken cancellationToken) => SendMessage(MessageType.Response, responseBytes, cancellationToken);
        public Stream Network { get; }
        public ILogger Logger { get; }
        public string Name { get; }

        private Task SendMessage(MessageType messageType, byte[] data, CancellationToken cancellationToken) => SendMessage(new WireMessage(messageType, data)).WaitAsync(cancellationToken);

        private async Task SendMessage(WireMessage wireMessage)
        {
            using (await _sendLock.LockAsync())
            {
                await Network.WriteMessage(wireMessage);
            }
        }
        
        public void Dispose() => Network.Dispose();

        private async Task ReceiveLoop()
        {
            WireMessage message;
            try
            {
                while (!(message = await Network.ReadMessage(_maxMessageSize)).Empty)
                {
                    var callback = message.MessageType == MessageType.Request ? RequestReceived : ResponseReceived;
                    if(callback != null)
                    {
                        var eventArgs = new DataReceivedEventsArgs(message.Data);
                        Task.Run(()=>callback.Invoke(this, eventArgs)).LogException(Logger, this);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"{nameof(ReceiveLoop)} {Name}");
            }
            Logger?.LogInformation($"{nameof(ReceiveLoop)} {Name} finished.");
            Dispose();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public readonly struct DataReceivedEventsArgs
    {
        public DataReceivedEventsArgs(byte[] data) => Data = data;
        public byte[] Data { get; }
    }
}