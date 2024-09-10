﻿namespace UiPath.Ipc;

partial class Telemetry
{
    public sealed partial record SystemError : RecordBase, IOperationFailed
    {
        public required ExceptionInfo Exception { get; init; }

        ExceptionInfo? IOperationFailed.Exception => Exception;
    }
}
