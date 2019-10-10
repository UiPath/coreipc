/* istanbul ignore file */
import { ILogicalSocketFactory, ILogicalSocket, LogicalSocket } from './logical-socket';
import { PhysicalSocket } from './physical-socket';
import { PipeClientStream } from './pipe-client-stream';
import { PipeReader } from './pipe-reader';
import { ISocketLike } from './socket-like';

export {
    ILogicalSocketFactory, ILogicalSocket,
    PhysicalSocket,
    PipeClientStream,
    PipeReader,
    LogicalSocket as SocketAdapter,
    ISocketLike
};
