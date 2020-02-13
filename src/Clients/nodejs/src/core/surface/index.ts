import {
    IpcClient,
    IIpcClientConfig,
    BeforeCallDelegate,
    ConnectionFactoryDelegate
} from './ipc-client';

import { __hasCancellationToken__, __returns__, __endpoint__ } from './rtti';
import { Message } from './message';
import { RemoteError } from './remote-error';

export {
    IpcClient,
    IIpcClientConfig,
    BeforeCallDelegate,
    ConnectionFactoryDelegate,
    __hasCancellationToken__, __returns__, __endpoint__,
    Message,
    RemoteError
};
