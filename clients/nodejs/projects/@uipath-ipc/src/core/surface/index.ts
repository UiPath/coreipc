import {
    IpcClient,
    IpcClientConfig,
    InternalIpcClientConfig,
    BeforeCallDelegate,
    ConnectionFactoryDelegate
} from './ipc-client';

import { __hasCancellationToken__, __returns__ } from './rtti';
import { Message } from './message';
import { RemoteError } from './remote-error';

export {
    IpcClient,
    IpcClientConfig,
    InternalIpcClientConfig,
    BeforeCallDelegate,
    ConnectionFactoryDelegate,
    __hasCancellationToken__, __returns__,
    Message,
    RemoteError
};
