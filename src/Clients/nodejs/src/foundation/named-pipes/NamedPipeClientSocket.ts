import * as net from 'net';

import {
    Observable,
    Subject,
} from 'rxjs';

import { Socket } from './Socket';
import { ConnectHelper, SocketLike } from '.';
import { Trace, argumentIs } from '../helpers';

import {
    PromiseCompletionSource,
    CancellationToken,
    TimeSpan,
} from '../threading';

import {
    InvalidOperationError,
    AggregateError,
    ObjectDisposedError,
    TimeoutError,
} from '../errors';

import { PipeNameConvention } from './PipeNameConvention';

/* @internal */
export class NamedPipeClientSocket extends Socket {
    public static async connectWithHelper(
        connectHelper: ConnectHelper,
        pipeName: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: new () => SocketLike,
    ) {
        let socket: NamedPipeClientSocket | undefined;
        const errors = new Array<Error>();
        let tryConnectCalled = false;

        await connectHelper({
            pipeName,
            timeout,
            ct,
            async tryConnect() {
                tryConnectCalled = true;
                if (socket) { return true; }

                try {
                    socket = await NamedPipeClientSocket.connect(pipeName, timeout, ct, socketLikeCtor);
                    return true;
                } catch (error) {
                    errors.push(error);
                    return false;
                }
            },
        });

        let error: Error | undefined;
        switch (errors.length) {
            case 0: break;
            case 1: error = errors[0]; break;
            default: error = new AggregateError(undefined, ...errors); break;
        }

        if (socket) {
            if (error) { Trace.log(error); }
            return socket;
        } else {
            if (error) { throw error; }
            if (!tryConnectCalled) { throw new InvalidOperationError(`The specified ConnectHelper didn't call the provided tryConnect function.`); }
            throw new InvalidOperationError();
        }
    }

    public static async connect(
        pipeName: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: new () => SocketLike,
    ): Promise<NamedPipeClientSocket> {
        argumentIs(pipeName, 'pipeName', 'string');
        argumentIs(timeout, 'timeout', TimeSpan);
        argumentIs(ct, 'ct', CancellationToken);
        argumentIs(socketLikeCtor, 'socketLikeCtor', 'undefined', Function);

        const path = PipeNameConvention.current.getFullName(pipeName);

        /* istanbul ignore next */
        const socketLike = new (socketLikeCtor ?? net.Socket)();
        const pcs = new PromiseCompletionSource<void>();

        console.log(`connecting to ${path}`);

        socketLike
            .once('error', error => {
                pcs.setFaulted(error);
            })
            .connect(path, () => {
                pcs.setResult();
            });
        const ctReg = ct.register(() => pcs.setCanceled());
        timeout.bind(pcs);

        try {
            await pcs.promise;
            return new NamedPipeClientSocket(socketLike);
        } catch (error) {
            socketLike.removeAllListeners();
            socketLike.unref();
            socketLike.destroy();

            throw error;
        } finally {
            ctReg.dispose();
        }
    }

    public get $data(): Observable<Buffer> { return this._$data; }
    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> {
        argumentIs(buffer, 'buffer', Buffer);
        argumentIs(ct, 'ct', CancellationToken);

        if (this._disposed) { throw new ObjectDisposedError('NamedPipeClientSocket'); }
        if (buffer.byteLength === 0) { return; }

        return await new Promise<void>((resolve, reject) => {
            this._socketLike.write(
                buffer,
                error => error
                    ? reject(error)
                    : resolve());
        });
    }

    public dispose(): void {
        if (this._disposed) { return; }
        this._disposed = true;

        this._$data.complete();
        this._socketLike.removeAllListeners();
        this._socketLike.unref();
        this._socketLike.destroy();
    }

    private constructor(private readonly _socketLike: SocketLike) {
        super();
        _socketLike.on('end', () => this.dispose());
        _socketLike.on('data', data => this._$data.next(data));
    }

    private readonly _$data = new Subject<Buffer>();
    private _disposed = false;
}
