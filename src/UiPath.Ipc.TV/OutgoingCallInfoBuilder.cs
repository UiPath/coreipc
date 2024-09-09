namespace UiPath.Ipc.TV;

internal sealed class OutgoingCallInfoBuilder
{
    public readonly record struct ProgressReport(int CTotal, int CProcessed);

    public static Task<OutgoingCallInfoResults> Build(RelationalTelemetryModel model, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var instance = new OutgoingCallInfoBuilder(model, progress, ct);
            instance.Run();
            return instance._output ?? throw new InvalidOperationException();
        });
    }

    private readonly RelationalTelemetryModel _input;
    private readonly IProgress<ProgressReport>? _progress;
    private readonly CancellationToken _ct;

    private OutgoingCallInfoResults? _output;

    private readonly Dictionary<string, Relational<Telemetry.ProcessStart>> _fileNameToProcessStart = new();
    private readonly Dictionary<string, InvokeRemoteProperInfo> _invokeRemoteProperInfos = new();
    private readonly Dictionary<string, ServiceClientInfo> _serviceClientInfos = new();

    private readonly List<string> _orderedInvokeRemoteProperIds = new();
    private readonly List<Exception> _exceptions = new();

    private OutgoingCallInfoBuilder(RelationalTelemetryModel input, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        _input = input;
        _progress = progress;
        _ct = ct;
    }

    private void Run()
    {
        Main();
        WrapUp();
    }

    private void WrapUp()
    {
        _ct.ThrowIfCancellationRequested();

        _output = new(
            Infos: _orderedInvokeRemoteProperIds.Select(Pal).Select(details => new OutgoingCallInfo { Details = details }).ToArray(),
            Exceptions: _exceptions);

        OutgoingCallDetails Pal(string invokeRemoteProperId)
        {
            if (!_invokeRemoteProperInfos.TryGetValue(invokeRemoteProperId, out var info))
            {
                throw new InvalidOperationException("InvokeRemoteProper not found.");
            }

            if (!_fileNameToProcessStart.TryGetValue(info.InvokeRemoteProper.RelationalRecord.Origin.File.Name, out var processStart))
            {
                throw new InvalidOperationException("ProcessStart not found.");
            }

            return new()
            {
                InvokeRemoteProper = info.InvokeRemoteProper,
                InvokeRemoteProperSucceded = info.InvokeRemoteProperSucceded,
                InvokeRemoteProperFailed = info.InvokeRemoteProperFailed,
                InvokeRemote = info.InvokeRemote ?? throw new InvalidOperationException("InvokeRemote not found."),
                ServiceClient = info.ServiceClientCreated ?? throw new InvalidOperationException("ServiceClientCreated not found."),
                EnsureConnection = info.Destination?.EnsureConnection,
                ProcessStart = processStart,
            };
        }
    }

    private void Main()
    {
        int index = 0;
        foreach (var record in _input.TimestampOrder)
        {
            _ct.ThrowIfCancellationRequested();

            try
            {
                Pal(record);
            }
            catch (Exception ex)
            {
                _exceptions.Add(ex);
            }

            index++;
            var report = new ProgressReport(CTotal: _input.TimestampOrder.Count, CProcessed: index);
            _progress?.Report(report);
        }

        void Pal(RelationalRecord record)
        {
            if (record.Is<Telemetry.ProcessStart>(out var processStart))
            {
                _fileNameToProcessStart[record.Origin.File.Name] = processStart;
                return;
            }

            if (record.Is<Telemetry.InvokeRemoteProper>(out var invokeRemoteProper))
            {
                if (!invokeRemoteProper.RelationalRecord.Has<Telemetry.InvokeRemote>(ForwardLinkType.Parent, out var invokeRemote))
                {
                    throw new ParentlessInvokeRemoteProper { InvokeRemoteProper = invokeRemoteProper };
                }

                if (!invokeRemote.RelationalRecord.Has<Telemetry.ServiceClientCreated>(ForwardLinkType.Parent, out var serviceClientCreated))
                {
                    throw new ParentlessInvokeRemote { InvokeRemote = invokeRemote };
                }

                _serviceClientInfos.TryGetValue(serviceClientCreated.Record.Id.Value, out var serviceClientInfo);

                var info = _invokeRemoteProperInfos.GetValueOrAdd(invokeRemoteProper.Record.Id.Value, _ => new() { InvokeRemoteProper = invokeRemoteProper });
                info.InvokeRemote = invokeRemote;
                info.ServiceClientCreated = serviceClientCreated;
                info.Destination = serviceClientInfo?.LatestConnect;

                _orderedInvokeRemoteProperIds.Add(invokeRemoteProper.Record.Id.Value);
                return;
            }

            if (record.Is<Telemetry.VoidSucceeded>(out var connectSucceeded) &&
                connectSucceeded.RelationalRecord.Has<Telemetry.Connect>(ForwardLinkType.Succeeded, out var connect))
            {
                if (!connect.RelationalRecord.Has<Telemetry.EnsureConnectionInitialState>(ForwardLinkType.Cause, out var ensureConnectionInitialState))
                {
                    throw new CauselessConnect(connect);
                }

                if (!ensureConnectionInitialState.RelationalRecord.Has<Telemetry.EnsureConnection>(ForwardLinkType.Cause, out var ensureConnection))
                {
                    throw new CauselessEnsureConnectionInitialState(ensureConnectionInitialState);
                }

                if (!ensureConnection.RelationalRecord.Has<Telemetry.ServiceClientCreated>(ForwardLinkType.Modified, out var serviceClientCreated))
                {
                    throw new UnboundEnsureConnection(ensureConnection);
                }

                var latestConnect = new ServiceClientDestination()
                {
                    EnsureConnection = ensureConnection,
                    EnsureConnectionInitialState = ensureConnectionInitialState,
                    Connect = connect,
                    ConnectSuccess = connectSucceeded
                };
                _serviceClientInfos.GetValueOrAdd(serviceClientCreated.Record.Id.Value, _ => new() { ServiceClientCreated = serviceClientCreated })
                    .LatestConnect = latestConnect;

                if (connect.RelationalRecord.Has<Telemetry.EnsureConnectionInitialState>(ForwardLinkType.Cause, out var ensureConnectionInitialState2) &&
                    ensureConnectionInitialState2.RelationalRecord.Has<Telemetry.EnsureConnection>(ForwardLinkType.Cause, out var ensureConnection2) &&
                    ensureConnection2.RelationalRecord.Has<Telemetry.InvokeRemoteProper>(ForwardLinkType.Cause, out var invokeRemoteProper3))
                {
                    if (!_invokeRemoteProperInfos.TryGetValue(invokeRemoteProper3.Record.Id, out var info))
                    {
                        throw new InvalidOperationException("Could not find InvokeRemoteProperInfo.");
                    }

                    info.Destination = latestConnect;
                }
                return;
            }

            if (record.Is<Telemetry.ResultSucceeded>(out var invokeRemoteProperSucceeded) &&
                invokeRemoteProperSucceeded.RelationalRecord.Has<Telemetry.InvokeRemoteProper>(ForwardLinkType.Succeeded, out var invokeRemoteProper2))
            {
                if (!_invokeRemoteProperInfos.TryGetValue(invokeRemoteProper2.Record.Id, out var info))
                {
                    throw new InvalidOperationException("Could not find InvokeRemoteProperInfo.");
                }

                info.InvokeRemoteProperSucceded = invokeRemoteProperSucceeded;
                return;
            }

            if (record.Is<Telemetry.VoidFailed>(out var invokeRemoteProperFailed) &&
                invokeRemoteProperFailed.RelationalRecord.Has<Telemetry.InvokeRemoteProper>(ForwardLinkType.Failed, out var invokeRemotePropert3))
            {
                if (!_invokeRemoteProperInfos.TryGetValue(invokeRemotePropert3.Record.Id, out var info))
                {
                    throw new InvalidOperationException("Could not find InvokeRemoteProperInfo.");
                }

                info.InvokeRemoteProperFailed = invokeRemoteProperFailed;
                return;
            }
        }
    }

    public sealed class ParentlessInvokeRemoteProper : Exception
    {
        public required Relational<Telemetry.InvokeRemoteProper> InvokeRemoteProper { get; init; }
    }
    public sealed class InvokeRemoteProperShouldHaveInvokeRemoteAsParent : Exception
    {
        public required Relational<Telemetry.InvokeRemoteProper> InvokeRemoteProper { get; init; }
    }
    public sealed class ParentlessInvokeRemote : Exception
    {
        public required Relational<Telemetry.InvokeRemote> InvokeRemote { get; init; }
    }

    public sealed class CauselessConnect : Exception
    {
        public Relational<Telemetry.Connect> Connect { get; }

        public CauselessConnect(Relational<Telemetry.Connect> connect)
        {
            Connect = connect;
        }
    }
    public sealed class CauselessEnsureConnectionInitialState : Exception
    {
        public Relational<Telemetry.EnsureConnectionInitialState> EnsureConnectionInitialState { get; }

        public CauselessEnsureConnectionInitialState(Relational<Telemetry.EnsureConnectionInitialState> ensureConnectionInitialState)
        {
            EnsureConnectionInitialState = ensureConnectionInitialState;
        }
    }

    public sealed class UnboundEnsureConnection : Exception
    {
        public Relational<Telemetry.EnsureConnection> EnsureConnection { get; }

        public UnboundEnsureConnection(Relational<Telemetry.EnsureConnection> ensureConnection)
        {
            EnsureConnection = ensureConnection;
        }
    }
}
