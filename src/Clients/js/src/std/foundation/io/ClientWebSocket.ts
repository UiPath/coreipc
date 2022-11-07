// #!if target === 'node'
import 'websocket-polyfill';
// #!endif

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

    PromiseCompletionSource,
    CancellationToken,
    TimeSpan,

    WebSocketAddress,
} from '@foundation';

import {
    Socket,
    ConnectHelper,
    WebSocketLike,
    WebSocketLikeCtor,
} from '.';

/* @internal */
export class WebSocketCtorCaller {
    public static call(ctor: WebSocketLikeCtor, address: WebSocketAddress): WebSocketLike {
        return new ctor(address.url, 'coreipc');
    }
}

/* @internal */
export class ClientWebSocket extends Socket {
    public static async connectWithHelper(
        connectHelper: ConnectHelper,
        address: WebSocketAddress,
        timeout: TimeSpan,
        ct: CancellationToken,
    ) {
        let socket: ClientWebSocket | undefined;
        const errors = new Array<any>();
        let tryConnectCalled = false;

        await connectHelper({
            address,
            timeout,
            ct,
            async tryConnect() {
                tryConnectCalled = true;
                if (socket) { return true; }

                try {
                    socket = await ClientWebSocket.connect(address, timeout, ct);
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
        address: WebSocketAddress,
        timeout: TimeSpan,
        ct: CancellationToken,
    ): Promise<ClientWebSocket> {
        argumentIs(address, 'address', 'string', URL, 'object');
        argumentIs(timeout, 'timeout', TimeSpan);
        argumentIs(ct, 'ct', CancellationToken);
        argumentIs(address.ctor, 'address.ctor', 'undefined', Function);

        /* istanbul ignore next */
        const defaultCtor: WebSocketLikeCtor = WebSocket as any;

        /* istanbul ignore next */
        const socketLike = WebSocketCtorCaller.call(address.ctor ?? defaultCtor, address);

        // #!if target !== 'node'
        socketLike.binaryType = 'arraybuffer';
        // #!endif

        const pcs = new PromiseCompletionSource<void>();

        socketLike.onerror = event => {
            pcs.setFaulted(new Error('Received error while awaiting a WebSocket to connect.'));
        };
        socketLike.onopen = event => {
            pcs.setResult();
        };


        const ctReg = ct.register(() => pcs.setCanceled());
        timeout.bind(pcs);

        try {
            await pcs.promise;
            return new ClientWebSocket(socketLike);
        } catch (error) {
            socketLike.onerror = null;
            socketLike.onopen = null;
            socketLike.close();

            throw error;
        } finally {
            ctReg.dispose();
        }
    }

    public get $data(): Observable<Buffer> { return this._$data; }
    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> {
        argumentIs(buffer, 'buffer', Buffer);
        argumentIs(ct, 'ct', CancellationToken);

        if (this._disposed) { throw new ObjectDisposedError('ClientWebSocket'); }
        if (buffer.byteLength === 0) { return; }

        return await new Promise<void>((resolve, reject) => {
            this._socketLike.send(buffer);
            resolve();
        });
    }

    public dispose(): void {
        if (this._disposed) { return; }
        this._disposed = true;

        this._$data.complete();
        this._socketLike.onopen = null;
        this._socketLike.onerror = null;
        this._socketLike.onclose = null;
        this._socketLike.onmessage = null;
        this._socketLike.close();
    }

    private constructor(private readonly _socketLike: WebSocketLike) {
        super();
        _socketLike.onclose = () => this.dispose();
        _socketLike.onmessage = event => {
            const buffer = Buffer.from(event.data as ArrayBuffer);
            this._$data.next(buffer);
        };
    }

    private readonly _$data = new Subject<Buffer>();
    private _disposed = false;
}
