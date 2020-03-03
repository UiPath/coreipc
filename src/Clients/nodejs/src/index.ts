import './foundation/threading/promise-pal';

import {
    CancellationTokenSource,
    PromiseCompletionSource,
    CancellationToken,
    TimeSpan,
    Timeout
} from './foundation/threading';

import {
    IpcClient,
    IIpcClientConfig,
    BeforeCallDelegate,
    ConnectionFactoryDelegate,
    __hasCancellationToken__,
    __returns__,
    __endpoint__,
    Message,
    RemoteError,
} from './core/surface';

import { IPipeClientStream } from './foundation/pipes';
import { OperationCanceledError, ObjectDisposedError } from './foundation/errors';
import { WireError as IpcError } from './core/internals';
import { Trace } from './foundation/utils';
import { IDisposable } from './foundation/disposable';

export {
    IpcClient,
    IIpcClientConfig,
    BeforeCallDelegate,
    ConnectionFactoryDelegate,
    __hasCancellationToken__,
    __returns__,
    __endpoint__,
    Message,
    RemoteError,

    CancellationToken,
    CancellationTokenSource,
    PromiseCompletionSource,
    IPipeClientStream,
    TimeSpan,
    Timeout,
    IpcError,
    ObjectDisposedError,
    OperationCanceledError,
    IDisposable,
    Trace,
};
