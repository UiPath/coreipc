import * as net from 'net';
import { Observable, Subject } from 'rxjs';
import { argumentIs, CancellationToken, TimeSpan, PromiseCompletionSource, ObjectDisposedError, TimeoutError } from '@foundation';
import { SocketLike, Socket } from '.';

/* @internal */
export class NamedPipeClientSocket extends Socket {
    public static async connect(
        path: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: new () => SocketLike,
    ): Promise<NamedPipeClientSocket> {
        argumentIs(path, 'path', 'string');
        argumentIs(timeout, 'timeout', TimeSpan);
        argumentIs(ct, 'ct', CancellationToken);
        argumentIs(socketLikeCtor, 'socketLikeCtor', 'undefined', Function);

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

    private readonly _$data = new Subject<Buffer>();
    private _disposed = false;

    private constructor(private readonly _socketLike: SocketLike) {
        super();
        _socketLike.on('end', () => this.dispose());
        _socketLike.on('data', data => this._$data.next(data));
    }

    public get $data(): Observable<Buffer> { return this._$data; }
    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> {
        argumentIs(buffer, 'buffer', Buffer);
        argumentIs(ct, 'ct', CancellationToken);

        if (this._disposed) { throw new ObjectDisposedError('NamedPipeClientSocket'); }
        if (buffer.byteLength === 0) { return; }

        return await new Promise<void>((resolve, reject) => this._socketLike.write(buffer, error => error ? reject(error) : resolve()));
    }

    public dispose(): void {
        if (this._disposed) { return; }
        this._disposed = true;

        this._$data.complete();
        this._socketLike.removeAllListeners();
        this._socketLike.unref();
        this._socketLike.destroy();
    }
}
