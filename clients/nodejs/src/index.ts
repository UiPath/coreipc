import './foundation/threading/promise-pal';

import {
    CancellationTokenSource,
    PromiseCompletionSource,
    CancellationToken,
    TimeSpan,
    Timeout
} from './foundation/threading';

import {
    __hasCancellationToken__, __returns__,
    RemoteError,
    IpcClient,
    Message
} from './core/surface';

import { IPipeClientStream } from './foundation/pipes';
import { OperationCanceledError } from './foundation/errors';
import { WireError as IpcError } from './core/internals';
import { Trace } from './foundation/utils';

export {
    CancellationToken,
    CancellationTokenSource,
    PromiseCompletionSource,
    IPipeClientStream,
    IpcClient,
    Message,
    TimeSpan,
    Timeout,
    RemoteError,
    IpcError,
    OperationCanceledError,
    __hasCancellationToken__,
    __returns__,
    Trace
};
