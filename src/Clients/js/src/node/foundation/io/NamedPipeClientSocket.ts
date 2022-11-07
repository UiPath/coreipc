import * as net from 'net';

import {
    Observable,
    Subject,
} from 'rxjs';

import {
    argumentIs,
    Trace,
    InvalidOperationError,
    AggregateError,
    ObjectDisposedError,
    UnknownError,

    TimeSpan,
    CancellationToken,
    PromiseCompletionSource,
} from '@foundation';

import {
    Socket,
    ConnectHelper,
    SocketLike,
} from '../../../std/foundation';

import {
    CoreIpcPlatform,
    NamedPipeSocketAddress,
    NamedPipeSocketLikeCtor,
} from '.';

/* @internal */
export class NamedPipeClientSocket extends Socket {
    public static async connectWithHelper(
        connectHelper: ConnectHelper,
        pipeName: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: NamedPipeSocketLikeCtor,
    ) {
        let socket: NamedPipeClientSocket | undefined;
        const errors = new Array<Error>();
        let tryConnectCalled = false;

        await connectHelper({
            address: new NamedPipeSocketAddress(pipeName),
            timeout,
            ct,
            async tryConnect() {
                tryConnectCalled = true;
                if (socket) { return true; }

                try {
                    socket = await NamedPipeClientSocket.connect(
                        new NamedPipeSocketAddress(pipeName, socketLikeCtor),
                        timeout,
                        ct);
                    return true;
                } catch (error) {
                    errors.push(UnknownError.ensureError(error));
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
        address: NamedPipeSocketAddress,
        timeout: TimeSpan,
        ct: CancellationToken,
        pipeTools?: CoreIpcPlatform,
    ): Promise<NamedPipeClientSocket> {
        argumentIs(address, 'address', NamedPipeSocketAddress);
        argumentIs(timeout, 'timeout', TimeSpan);
        argumentIs(ct, 'ct', CancellationToken);
        argumentIs(address.ctor, 'address.ctor', 'undefined', Function);
        argumentIs(pipeTools, 'pipeTools', 'undefined', CoreIpcPlatform);

        const path = (pipeTools ?? CoreIpcPlatform.current).getFullPipeName(address.pipeName);

        /* istanbul ignore next */
        const socketLike = new (address.ctor ?? net.Socket)();
        const pcs = new PromiseCompletionSource<void>();

        socketLike
            .once('error', (error: any) => {
                pcs.setFaulted(UnknownError.ensureError(error));
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
