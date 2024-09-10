﻿namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record ServerConnectionListenCancel : RecordBase, Is<Modifier>, IOperationStart
    {
        public required Id<ServerConnectionListen> ServerConnectionListenId { get; init; }

        Id? Is<Modifier>.Of => ServerConnectionListenId;
    }
}
