import * as net from 'net';

import { Observable, Subject } from 'rxjs';

import {
    assertArgument,
    Trace,
    InvalidOperationError,
    AggregateError,
    ObjectDisposedError,
    PromiseCompletionSource,
    CancellationToken,
    TimeSpan,
    Socket,
    ConnectHelper,
    UnknownError,
    PromisePal,
} from '../../../std';

import { Platform } from '../..';

import { NamedPipeAddress, NamedPipeSocketLikeCtor, NamedPipeSocketLike } from '.';

/* @internal */
export class NamedPipeSocket extends Socket {
    public static async connectWithHelper(
        connectHelper: ConnectHelper<NamedPipeAddress>,
        pipeName: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: NamedPipeSocketLikeCtor,
    ) {
        let socket: NamedPipeSocket | undefined;
        const errors = new Array<Error>();
        let tryConnectCalled = false;

        await connectHelper({
            address: new NamedPipeAddress(pipeName),
            timeout,
            ct,
            async tryConnectAsync() {
                tryConnectCalled = true;
                if (socket) {
                    return true;
                }

                try {
                    socket = await NamedPipeSocket.connectRetrying(pipeName, timeout, ct, socketLikeCtor);
                    return true;
                } catch (error) {
                    errors.push(UnknownError.ensureError(error));
                    return false;
                }
            },
        });

        const error = AggregateError.maybeAggregate(...errors);

        if (error) {
            Trace.log(error);
        }

        if (socket) {
            return socket;
        }

        if (error) {
            throw error;
        }

        if (!tryConnectCalled) {
            throw new InvalidOperationError(
                `The specified ConnectHelper didn't call the provided tryConnect function.`,
            );
        }

        throw new InvalidOperationError();
    }

    // Waits (ms) after each failed attempt; the last repeats until the timeout or cancellation.
    private static readonly connectRetryDelays = [10, 20, 50, 100];

    // Like .NET, a pipe the server has not created yet (or a stale socket file) is retried
    // within the call's timeout instead of failing the first attempt.
    public static async connectRetrying(
        pipeName: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: NamedPipeSocketLikeCtor,
    ): Promise<NamedPipeSocket> {
        const deadline = timeout.isNegative
            ? Number.POSITIVE_INFINITY
            : Date.now() + timeout.totalMilliseconds;

        for (let attempt = 0; ; attempt++) {
            const remaining = deadline - Date.now();
            const attemptTimeout = Number.isFinite(remaining)
                ? TimeSpan.fromMilliseconds(Math.max(remaining, 0))
                : timeout;

            try {
                return await NamedPipeSocket.connect(pipeName, attemptTimeout, ct, socketLikeCtor);
            } catch (error) {
                const delays = NamedPipeSocket.connectRetryDelays;
                const delay = delays[Math.min(attempt, delays.length - 1)];
                if (
                    !NamedPipeSocket.isTransientConnectError(error) ||
                    ct.isCancellationRequested ||
                    Date.now() + delay >= deadline
                ) {
                    throw error;
                }
                await PromisePal.delay(delay, ct);
            }
        }
    }

    private static isTransientConnectError(error: unknown): boolean {
        const code = (error as { code?: unknown } | undefined)?.code;
        return code === 'ENOENT' || code === 'ECONNREFUSED';
    }

    public static async connect(
        pipeName: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: new () => NamedPipeSocketLike,
    ): Promise<NamedPipeSocket> {
        assertArgument({ pipeName }, 'string');
        assertArgument({ timeout }, TimeSpan);
        assertArgument({ ct }, CancellationToken);
        assertArgument({ socketLikeCtor }, 'undefined', Function);

        const path = Platform.current.getFullPipeName(pipeName);

        /* istanbul ignore next */
        const socketLike = new (socketLikeCtor ?? net.Socket)();
        const pcs = new PromiseCompletionSource<void>();

        socketLike
            .once('error', (error) => {
                pcs.setFaulted(error);
            })
            .connect(path, () => {
                pcs.setResult();
            });
        const ctReg = ct.register(() => pcs.setCanceled());
        timeout.bind(pcs);

        try {
            await pcs.promise;
            return new NamedPipeSocket(socketLike);
        } catch (error) {
            socketLike.removeAllListeners();
            socketLike.unref();
            socketLike.destroy();

            throw error;
        } finally {
            ctReg.dispose();
        }
    }

    public get $data(): Observable<Buffer> {
        return this._$data;
    }
    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> {
        assertArgument({ buffer }, Buffer);
        assertArgument({ ct }, CancellationToken);

        if (this._disposed) {
            throw new ObjectDisposedError(NamedPipeSocket.name);
        }

        if (buffer.byteLength === 0) {
            return;
        }

        return await new Promise<void>((resolve, reject) => {
            this._socketLike.write(buffer, (error) => (error ? reject(error) : resolve()));
        });
    }

    public dispose(): void {
        if (this._disposed) {
            return;
        }
        this._disposed = true;

        this._$data.complete();
        this._socketLike.removeAllListeners();
        this._socketLike.unref();
        this._socketLike.destroy();
    }

    private constructor(private readonly _socketLike: NamedPipeSocketLike) {
        super();
        _socketLike.on('end', () => this.dispose());
        _socketLike.on('data', (data) => this._$data.next(data));
    }

    private readonly _$data = new Subject<Buffer>();
    private _disposed = false;
}
