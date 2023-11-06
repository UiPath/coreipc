import { Observer } from 'rxjs';
import {
    CancellationToken,
    TimeSpan,
    Trace,
    ArgumentOutOfRangeError,
    SocketStream,
    UnknownError,
} from '../../../bcl';

import { ConnectHelper, Address } from '../..';
import { MessageStream, IMessageStream, Network } from '..';
import { IRpcChannel, RpcCallContext, RpcMessage } from '.';

/* @internal */
export class RpcChannel implements IRpcChannel {
    public static create(
        address: Address,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStream.Factory,
    ): IRpcChannel {
        return new RpcChannel(
            address,
            connectHelper,
            connectTimeout,
            ct,
            observer,
            messageStreamFactory,
        );
    }

    constructor(
        address: Address,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        private readonly _observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStream.Factory,
    ) {
        this._$messageStream = RpcChannel.createMessageStream(
            address,
            connectHelper,
            connectTimeout,
            ct,
            this._networkObserver,
            MessageStream.Factory.orDefault(messageStreamFactory),
        );

        this._$messageStream.catch((_) => {
            const __ = this.disposeAsync();
        });
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            this._isDisposed = true;
            try {
                const messageStream = await this._$messageStream;
                await messageStream.disposeAsync();
            } catch (_) {}
        }
    }

    public async call(
        request: RpcMessage.Request,
        timeout: TimeSpan,
        ct: CancellationToken,
    ): Promise<RpcMessage.Response> {
        const promise = this._outgoingCalls.register(request, timeout, ct);
        await (await this._$messageStream).writeMessageAsync(request.toNetwork(), ct);
        return await promise;
    }

    public get isDisposed(): boolean {
        return this._isDisposed;
    }

    private _isDisposed = false;

    private static async createMessageStream(
        address: Address,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        networkObserver: Observer<Network.Message>,
        messageStreamFactory: IMessageStream.Factory,
    ): Promise<IMessageStream> {
        const socket = await address.connect(connectHelper, connectTimeout, ct);

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

    private readonly _networkObserver = new (class implements Observer<Network.Message> {
        constructor(private readonly _owner: RpcChannel) {}

        public closed?: boolean;
        public next(value: Network.Message): void {
            this._owner.dispatchNetworkUnit(value);
        }
        public error(err: any): void {
            this._owner.dispatchNetworkError(err);
        }
        public complete(): void {
            this._owner.dispatchNetworkComplete();
        }
    })(this);

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
                        `Expecting either one of Network.Message.Type.Request, Network.Message.Type.Response, Network.Message.Type.Cancel but got ${message.type}.`,
                    ),
                );
                break;
        }
    }
    private dispatchNetworkError(err: any): void {
        this._observer.error(err);
    }
    private dispatchNetworkComplete(): void {
        Trace.traceErrorRethrow(this.disposeAsync());
    }

    private processIncommingResponse(message: Network.Message): void {
        const rpcResponse = RpcMessage.Response.fromNetwork(message);

        this._outgoingCalls.tryComplete(rpcResponse);
    }

    private processIncommingRequest(message: Network.Message): void {
        const request = RpcMessage.Request.fromNetwork(message);
        const context = new RpcCallContext.Incomming(request, async (response) => {
            response.RequestId = request.Id;
            await (
                await this._$messageStream
            ).writeMessageAsync(response.toNetwork(), CancellationToken.none);
        });
        try {
            this._observer.next(context);
        } catch (err) {
            Trace.log(UnknownError.ensureError(err));
        }
    }

    private processIncommingCancellationRequest(message: Network.Message): void {
        throw new Error('Method not implemented.');
    }

    private static readonly OutgoingCallTable = class {
        private _sequence = 0;
        private readonly _map = new Map<string, RpcCallContext.Outgoing>();

        public async register(
            request: RpcMessage.Request,
            timeout: TimeSpan,
            ct: CancellationToken,
        ): Promise<RpcMessage.Response> {
            request.Id = this.generateId();

            const context = new RpcCallContext.Outgoing(timeout, ct);
            this._map.set(request.Id, context);

            try {
                return await context.promise;
            } finally {
                this._map.delete(request.Id);
            }
        }

        public tryComplete(response: RpcMessage.Response) {
            this._map.get(response.RequestId)?.complete(response);
        }

        private generateId(): string {
            return `${this._sequence++}`;
        }
    };
}
