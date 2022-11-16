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
} from '../std';

import {
    BrowserWebSocketAddress,
    BrowserWebSocketError,
    BrowserWebSocketLike,
    BrowserWebSocketLikeCtor,
} from '.';

/* @internal */
export class BrowserWebSocket extends Socket {
    public static async connectWithHelper(
        connectHelper: ConnectHelper<BrowserWebSocketAddress>,
        url: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: BrowserWebSocketLikeCtor
    ) {
        let socket: BrowserWebSocket | undefined;
        const errors = new Array<Error>();
        let tryConnectCalled = false;

        await connectHelper({
            address: new BrowserWebSocketAddress(url),
            timeout,
            ct,
            async tryConnect() {
                tryConnectCalled = true;
                if (socket) {
                    return true;
                }

                try {
                    socket = await BrowserWebSocket.connect(
                        url,
                        timeout,
                        ct,
                        socketLikeCtor
                    );
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
                `The specified ConnectHelper didn't call the provided tryConnect function.`
            );
        }

        throw new InvalidOperationError();
    }

    public static async connect(
        url: string,
        timeout: TimeSpan,
        ct: CancellationToken,
        socketLikeCtor?: BrowserWebSocketLikeCtor
    ): Promise<BrowserWebSocket> {
        assertArgument({ url }, 'string');
        assertArgument({ timeout }, TimeSpan);
        assertArgument({ ct }, CancellationToken);
        assertArgument({ socketLikeCtor }, 'undefined', Function);

        const WebSocketCtor = socketLikeCtor ?? WebSocket;

        /* istanbul ignore next */
        const socket = new WebSocketCtor(url, 'coreipc');
        socket.binaryType = 'arraybuffer';

        const pcs = new PromiseCompletionSource<void>();

        socket.onerror = (event) => {
            const error = new BrowserWebSocketError.ConnectFailure(socket);
            pcs.trySetFaulted(error);
        };

        socket.onopen = (event) => {
            pcs.trySetResult();
        };

        const ctReg = ct.register(() => pcs.trySetCanceled());
        timeout.bind(pcs);

        try {
            await pcs.promise;
            return new BrowserWebSocket(socket);
        } catch (error) {
            socket.onerror = null;
            socket.onopen = null;
            socket.close();

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
            throw new ObjectDisposedError(BrowserWebSocket.name);
        }

        if (buffer.byteLength === 0) {
            return;
        }

        return await new Promise<void>((resolve) => {
            this._socket.send(buffer);
            resolve();
        });
    }

    public dispose(): void {
        if (this._disposed) {
            return;
        }
        this._disposed = true;

        this._$data.complete();
        this._socket.onopen = null;
        this._socket.onerror = null;
        this._socket.onclose = null;
        this._socket.onmessage = null;
        this._socket.close();
    }

    private constructor(private readonly _socket: BrowserWebSocketLike) {
        super();
        _socket.onclose = () => this.dispose();
        _socket.onmessage = (event) => {
            const buffer = Buffer.from(event.data as ArrayBuffer);
            this._$data.next(buffer);
        };
    }

    private readonly _$data = new Subject<Buffer>();
    private _disposed = false;
}
