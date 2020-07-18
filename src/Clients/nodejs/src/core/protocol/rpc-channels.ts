// tslint:disable: no-namespace no-internal-module variable-name no-shadowed-variable

import { Observable, Subject } from 'rxjs';
import { CancellationToken, IAsyncDisposable, argumentIs, PromiseCompletionSource, JsonConvert, TimeSpan, NamedPipeClientSocket, SocketStream, ConnectHelper } from '@foundation';
import { MessageStream, MessageEmitter, Network } from '.';

/* @internal */
export interface RpcChannel extends IAsyncDisposable {
    call(request: RpcMessage.Request, ct: CancellationToken): Promise<RpcMessage.Response>;
    $incommingCall: Observable<RpcCallContext.Incomming>;
}

/* @internal */
export abstract class RpcCallContextBase {
}

/* @internal */
export type RpcCallContext = RpcCallContext.Incomming | RpcCallContext.Outgoing;

/* @internal */
export module RpcCallContext {
    export class Incomming extends RpcCallContextBase {
    }

    export class Outgoing extends RpcCallContextBase {
        private readonly _pcs = new PromiseCompletionSource<RpcMessage.Response>();

        public get promise(): Promise<RpcMessage.Response> { return this._pcs.promise; }

        public complete(response: RpcMessage.Response): void {
            const _ = this._pcs.trySetResult(response);
        }

        public tryCancel(): void {
            const _ = this._pcs.trySetCanceled();
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
    }

    export class Response extends RpcMessageBase {
        public static fromNetwork(message: Network.Message): Response {
            return JsonConvert.deserializeObject(message.data.toString(), Response);
        }

        public constructor(
            public readonly RequestId: string,
            public readonly Data: string | null,
            public readonly Error: RpcError | null,
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

/* @internal */
export class RpcError {
    public constructor(
        public readonly Message: string,
        public readonly StackTrace: string,
        public readonly Type: string,
        public readonly InnerError: RpcError | null,
    ) {
    }
}

/* @internal */
export module RpcChannel {
    export class Impl implements RpcChannel {
        public static async create(messageStream: MessageStream, messageEmitter?: MessageEmitter): Promise<Impl> {
            argumentIs(messageStream, 'messageStream', Object);
            argumentIs(messageEmitter, 'messageEmitter', 'undefined', Object);

            let createdEmitter: MessageEmitter | null = null;
            try {
                if (!messageEmitter) {
                    messageEmitter = createdEmitter = MessageEmitter.Impl.create(messageStream);
                }

                return new Impl(messageStream, messageEmitter);
            } catch (error) {
                if (createdEmitter) {
                    await createdEmitter.disposeAsync();
                }
                throw error;
            }
        }

        public static async connect(pipeName: string, connectHelper: ConnectHelper, connectTimeout: TimeSpan, ct: CancellationToken): Promise<Impl> {
            const socket = await NamedPipeClientSocket.connectWithHelper(connectHelper, pipeName, connectTimeout, ct);
            try {
                const stream = new SocketStream(socket);
                try {
                    const network = new Network.Impl(stream);
                    try {
                        return await RpcChannel.Impl.create(network);
                    } catch (error) {
                        network.dispose();
                        throw error;
                    }
                } catch (error) {
                    stream.dispose();
                    throw error;
                }
            } catch (error) {
                socket.dispose();
                throw error;
            }
        }

        public disposeAsync(): Promise<void> {
            throw new Error('Method not implemented.');
        }

        public call(request: RpcMessage.Request, ct: CancellationToken): Promise<RpcMessage.Response> {
            const promise = this._outgoingCalls.register(request, ct);
            this._messageStream.write(request.toNetwork(), ct);
            return promise;
        }
        public get $incommingCall(): Observable<RpcCallContext.Incomming> { return this._$incommingCall; }

        public get isAlive(): boolean { throw null; }

        private constructor(
            private readonly _messageStream: MessageStream,
            private readonly _messageEmitter: MessageEmitter,
        ) {
            this._messageEmitter.$incommingMessage.subscribe(
                this.dispatchIncommingMessage,
                undefined,
                this.observeIncommingMessageCompletion,
            );
        }

        private readonly _outgoingCalls = new OutgoingCallTable();
        private readonly _$incommingCall = new Subject<RpcCallContext.Incomming>();

        private readonly dispatchIncommingMessage = (message: Network.Message): void => {
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
                    break;
            }
        }
        private readonly observeIncommingMessageCompletion = (): void => {
        }

        private processIncommingResponse(message: Network.Message): void {
            this._outgoingCalls.tryComplete(RpcMessage.Response.fromNetwork(message));
        }

        private processIncommingRequest(message: Network.Message): void {
            throw new Error('Method not implemented.');
        }

        private processIncommingCancellationRequest(message: Network.Message): void {
            throw new Error('Method not implemented.');
        }
    }

    class OutgoingCallTable {
        private _sequence = 0;
        private readonly _map = new Map<string, RpcCallContext.Outgoing>();

        public async register(request: RpcMessage.Request, ct: CancellationToken): Promise<RpcMessage.Response> {
            request.Id = this.generateId();
            const context = new RpcCallContext.Outgoing();
            this._map.set(request.Id, context);

            const reg = ct.register(() => { context.tryCancel(); });

            try {
                return await context.promise;
            } finally {
                reg.dispose();
            }
        }

        public tryComplete(response: RpcMessage.Response) { this._map.get(response.RequestId)?.complete(response); }

        private generateId(): string { return `${this._sequence++}`; }
    }
}
