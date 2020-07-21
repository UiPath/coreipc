import * as net from 'net';
import { Observable, Subject } from 'rxjs';
import {
    argumentIs,
    TimeSpan,
    PromiseCompletionSource,
    CancellationToken,
    ObjectDisposedError,
    InvalidOperationError,
    TimeoutError,
} from '@foundation';
import { SocketLike, Socket } from '.';
import { ConnectHelper } from './ConnectHelper';
import { Trace } from '../helpers';
import { AggregateError } from '../errors/AggregateError';
import { } from '@foundation';

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

        await connectHelper(async () => {
            tryConnectCalled = true;
            if (socket) { return true; }

            try {
                socket = await NamedPipeClientSocket.connect(pipeName, timeout, ct, socketLikeCtor);
                return true;
            } catch (error) {
                errors.push(error);
                return false;
            }
        }, pipeName, timeout, ct);

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

        const path = `\\\\.\\pipe\\${pipeName}`;

        /* istanbul ignore next */
        const socketLike = new (socketLikeCtor ?? net.Socket)();
        const pcs = new PromiseCompletionSource<void>();

        socketLike
            .once('error', error => pcs.setFaulted(error))
            .connect(path, () => pcs.setResult());

        const ctReg = ct.register(() => pcs.setCanceled());
        const timeoutReg = timeout.isInfinite
            ? null
            : setTimeout(() => pcs.setFaulted(new TimeoutError()), timeout.totalMilliseconds)
            ;

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
            if (timeoutReg) { clearTimeout(timeoutReg); }
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
