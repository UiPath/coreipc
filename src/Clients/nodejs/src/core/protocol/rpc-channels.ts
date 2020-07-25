// tslint:disable: no-namespace no-internal-module variable-name no-shadowed-variable

import { Observer } from 'rxjs';
import * as util from 'util';
import {
    PromiseCompletionSource,
    CancellationToken,
    NamedPipeClientSocket,
    SocketStream,
    ConnectHelper,
    TimeSpan,
    JsonConvert,
    Trace,
    ArgumentOutOfRangeError,
    IAsyncDisposable,
} from '../../foundation';

import {
    MessageStreamFactory,
    IMessageStreamFactory,
    IMessageStream,
    Network,
} from '.';

/* @internal */
export interface IRpcChannel extends IAsyncDisposable {
    readonly isDisposed: boolean;
    call(request: RpcMessage.Request, timeout: TimeSpan, ct: CancellationToken): Promise<RpcMessage.Response>;
}

/* @internal */
export interface IRpcChannelFactory {
    create(
        pipeName: string,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStreamFactory,
    ): IRpcChannel;
}

/* @internal */
export class RpcChannel implements IRpcChannel {
    public static create(
        pipeName: string,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStreamFactory,
    ): IRpcChannel {
        return new RpcChannel(
            pipeName,
            connectHelper,
            connectTimeout,
            ct,
            observer,
            messageStreamFactory,
        );
    }

    constructor(
        pipeName: string,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        private readonly _observer: Observer<RpcCallContext.Incomming>,

        messageStreamFactory?: IMessageStreamFactory,
    ) {
        this._$messageStream = RpcChannel.createMessageStream(
            pipeName,
            connectHelper,
            connectTimeout,
            ct,
            this._networkObserver,
            MessageStreamFactory.orDefault(messageStreamFactory));
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            this._isDisposed = true;
            await (await this._$messageStream).disposeAsync();
        }
    }

    public async call(request: RpcMessage.Request, timeout: TimeSpan, ct: CancellationToken): Promise<RpcMessage.Response> {
        const promise = this._outgoingCalls.register(request, timeout, ct);
        await (await this._$messageStream).writeMessageAsync(request.toNetwork(), ct);
        return await promise;
    }

    public get isDisposed(): boolean { return this._isDisposed; }

    private _isDisposed = false;

    private static async createMessageStream(
        pipeName: string,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        networkObserver: Observer<Network.Message>,
        messageStreamFactory: IMessageStreamFactory): Promise<IMessageStream> {

        const socket = await NamedPipeClientSocket.connectWithHelper(connectHelper, pipeName, connectTimeout, ct);
        try {
            const stream = new SocketStream(socket);
            try {
                return messageStreamFactory.create(stream, networkObserver);
            } catch (error) {
                stream.dispose();
                throw error;
            }
        } catch (error) {
            socket.dispose();
            throw error;
        }
    }

    private readonly _$messageStream: Promise<IMessageStream>;
    private readonly _outgoingCalls = new RpcChannel.OutgoingCallTable();

    private readonly _networkObserver = new class implements Observer<Network.Message> {
        constructor(private readonly _owner: RpcChannel) { }

        public closed?: boolean;
        public next(value: Network.Message): void { this._owner.dispatchNetworkUnit(value); }
        public error(err: any): void { this._owner.dispatchNetworkError(err); }
        public complete(): void { this._owner.dispatchNetworkComplete(); }
    }(this);

    private dispatchNetworkUnit(message: Network.Message): void {
        switch (message.type) {
            case Network.Message.Type.Response:
                this.processIncommingResponse(message);
                break;
            case Network.Message.Type.Request:
                this.processIncommingRequest(message);
                break;
            case Network.Message.Type.Cancel:
                this.processIncommingCancellationRequest(message);
                break;
            default:
                this._observer.error(
                    new ArgumentOutOfRangeError(
                        'message',
                        `Expecting either one of Network.Message.Type.Request, Network.Message.Type.Response, Network.Message.Type.Cancel but got ${message.type}.`));
                break;
        }
    }
    private dispatchNetworkError(err: any): void { this._observer.error(err); }
    private dispatchNetworkComplete(): void { this.disposeAsync().traceError(); }

    private processIncommingResponse(message: Network.Message): void {
        this._outgoingCalls.tryComplete(RpcMessage.Response.fromNetwork(message));
    }

    private processIncommingRequest(message: Network.Message): void {
        const request = RpcMessage.Request.fromNetwork(message);
        const context = new RpcCallContext.Incomming(
            request,
            async response => {
                response.RequestId = request.Id;
                await (await this._$messageStream).writeMessageAsync(
                    response.toNetwork(),
                    CancellationToken.none);
            },
        );
        try {
            this._observer.next(context);
        } catch (err) {
            Trace.log(err);
        }
    }

    private processIncommingCancellationRequest(message: Network.Message): void {
        throw new Error('Method not implemented.');
    }

    private static readonly OutgoingCallTable = class {
        private _sequence = 0;
        private readonly _map = new Map<string, RpcCallContext.Outgoing>();

        public async register(request: RpcMessage.Request, timeout: TimeSpan, ct: CancellationToken): Promise<RpcMessage.Response> {
            request.Id = this.generateId();

            const context = new RpcCallContext.Outgoing(timeout, ct);
            this._map.set(request.Id, context);

            try {
                return await context.promise;
            } finally {
                this._map.delete(request.Id);
            }
        }

        public tryComplete(response: RpcMessage.Response) { this._map.get(response.RequestId)?.complete(response); }

        private generateId(): string { return `${this._sequence++}`; }
    };
}

/* @internal */
export const defaultRpcChannelFactory: IRpcChannelFactory = RpcChannel;

/* @internal */
export abstract class RpcCallContextBase {
}

/* @internal */
export type RpcCallContext = RpcCallContext.Incomming | RpcCallContext.Outgoing;

/* @internal */
export module RpcCallContext {
    export class Incomming extends RpcCallContextBase {
        constructor(
            public readonly request: RpcMessage.Request,
            public readonly respond: (response: RpcMessage.Response) => Promise<void>,
        ) {
            super();
        }
    }

    export class Outgoing extends RpcCallContextBase {
        private readonly _pcs = new PromiseCompletionSource<RpcMessage.Response>();

        constructor(timeout: TimeSpan, ct: CancellationToken) {
            super();
            timeout.bind(this._pcs);
            ct.bind(this._pcs);
        }

        public get promise(): Promise<RpcMessage.Response> { return this._pcs.promise; }

        public complete(response: RpcMessage.Response): void {
            const _ = this._pcs.trySetResult(response);
        }
    }
}

/* @internal */
export abstract class RpcMessageBase {
    public abstract toNetwork(): Network.Message;
}

/* @internal */
export type RpcMessage =
    RpcMessage.Request |
    RpcMessage.Response |
    RpcMessage.CancellationRequest
    ;

/* @internal */
export type IncommingInitiatingRpcMessage =
    RpcMessage.Request |
    RpcMessage.CancellationRequest
    ;

/* @internal */
export module RpcMessage {
    export class Request extends RpcMessageBase {
        public constructor(
            public readonly TimeoutInSeconds: number,
            public readonly Endpoint: string,
            public readonly MethodName: string,
            public readonly Parameters: string[],
        ) { super(); }

        public Id: string = '';

        public toNetwork(): Network.Message {
            return {
                type: Network.Message.Type.Request,
                data: Buffer.from(JsonConvert.serializeObject(this)),
            };
        }

        public static fromNetwork(message: Network.Message): Request {
            return JsonConvert.deserializeObject(message.data.toString(), Request);
        }
    }

    export class Response extends RpcMessageBase {
        public static fromNetwork(message: Network.Message): Response {
            return JsonConvert.deserializeObject(message.data.toString(), Response);
        }

        public constructor(
            public RequestId: string,
            public readonly Data: string | null,
            public readonly Error: IpcError | null,
        ) { super(); }

        public toNetwork(): Network.Message {
            return {
                type: Network.Message.Type.Response,
                data: Buffer.from(JsonConvert.serializeObject(this)),
            };
        }
    }

    export class CancellationRequest extends RpcMessageBase {
        public static fromNetwork(message: Network.Message): CancellationRequest {
            return JsonConvert.deserializeObject(message.data.toString(), CancellationRequest);
        }

        public constructor(
            public readonly RequestId: string,
        ) { super(); }

        public toNetwork(): Network.Message {
            return {
                type: Network.Message.Type.Cancel,
                data: Buffer.from(JsonConvert.serializeObject(this)),
            };
        }
    }
}

export class IpcError {
    public constructor(
        public readonly Message: string,
        public readonly StackTrace: string,
        public readonly Type: string,
        public readonly InnerError: IpcError | null,
    ) {
    }
}
