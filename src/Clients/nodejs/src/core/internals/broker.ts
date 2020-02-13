import * as BrokerMessage from './broker-message';
import * as WireMessage from './wire-message';

import { ConnectionFactoryDelegate, BeforeCallDelegate } from '../surface';

import { CancellationTokenSource, CancellationToken } from '../../foundation/threading';
import { PipeClientStream, IPipeClientStream } from '../../foundation/pipes';
import { Trace } from '../../foundation/utils';
import { IAsyncDisposable } from '../../foundation/disposable';
import { InvalidOperationError, ArgumentError, ObjectDisposedError } from '../../foundation/errors';
import { Succeeded } from '../../foundation/utils';
import { ILogicalSocketFactory } from '../../foundation/pipes';
import { TimeSpan } from '../../foundation/threading';
import { ArgumentNullError } from '../../foundation/errors';
import { Maybe, MethodContainer } from '../../foundation/utils';

import { MessageEvent } from './message-event';
import { SerializationPal } from './serialization-pal';
import { CallbackContext, CallContextTable } from './context';
import { MessageStream, IMessageStream } from './message-stream';

/* @internal */
export interface IBroker extends IAsyncDisposable {
    sendReceiveAsync(brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response>;
}

/* @internal */
export interface IBrokerCtorParams {
    // mandatory
    readonly factory: ILogicalSocketFactory;
    readonly pipeName: string;
    readonly connectTimeout: TimeSpan;
    readonly defaultCallTimeout: TimeSpan;

    // optional
    readonly callback?: any;
    readonly connectionFactory?: Maybe<ConnectionFactoryDelegate>;
    readonly beforeCall?: Maybe<BeforeCallDelegate>;
    readonly traceNetwork?: boolean;
}

/* @internal */
export class Broker implements IBroker, IAsyncDisposable {
    private readonly _inFlightCallbackPromises = new Array<Promise<void>>();
    private _isDisposed = false;
    private readonly _cts = new CancellationTokenSource();

    private _maybeMessageStream: IMessageStream | null = null;
    private readonly _table = new CallContextTable();

    private _newConnection = true;
    private _insideConnectionFactory = false;
    private _insideBeforeCall = false;

    private static readonly _trace = Trace.category('Broker');

    constructor(private readonly _parameters: IBrokerCtorParams) {
        if (!_parameters) { throw new ArgumentNullError('_parameters'); }
        if (!_parameters.factory) { throw new ArgumentError('_parameters.factory cannot be null or undefined.', '_parameters'); }
        if (!_parameters.pipeName) { throw new ArgumentError('_parameters.factory cannot be null or undefined or an empty string.', '_parameters'); }
        if (!_parameters.connectTimeout) { throw new ArgumentError('_parameters.connectTimeout cannot be null or undefined.', '_parameters'); }
        if (!_parameters.defaultCallTimeout) { throw new ArgumentError('_parameters.defaultCallTimeout cannot be null or undefined.', '_parameters'); }
    }

    public async sendReceiveAsync(brokerRequest: BrokerMessage.OutboundRequest): Promise<BrokerMessage.Response> {
        if (!brokerRequest) { throw new ArgumentNullError('brokerRequest'); }
        if (this._isDisposed) { throw new ObjectDisposedError('Broker'); }

        const dataFactory = SerializationPal.prepareCallContext(brokerRequest, this._parameters.defaultCallTimeout, this._cts.token);
        const context = this._table.createContext(dataFactory);

        try {
            await this.ensureConnectedAsync(context.cancellationToken);
            await this.sendAsync(context.wireRequest, context.cancellationToken);
        } catch (error) {
            Broker._trace.log(`method "sendReceiveAsync": ensureConnectedAsync or sendAsync threw ${error}`);
            throw error;
        }

        return await context.promise;
    }

    private static readonly traceCategory = Trace.category('broker');

    private async getOrCreateMessageStreamAsync(cancellationToken: CancellationToken): Promise<IMessageStream> {
        if (!this._maybeMessageStream) {
            const messageStream = new MessageStream(await this.connectAsync(cancellationToken));
            Broker._trace.log(`method "getOrCreateMessageStreamAsync": created messageStream`);
            messageStream.messages.subscribe(this.processInboundAsync.bind(this));
            this._maybeMessageStream = messageStream;
        }
        return this._maybeMessageStream;
    }
    private connectCoreAsync(cancellationToken: CancellationToken): Promise<IPipeClientStream> {
        return PipeClientStream.connectAsync(
            this._parameters.factory,
            this._parameters.pipeName,
            this._parameters.connectTimeout,
            !!this._parameters.traceNetwork,
            cancellationToken
        );
    }
    private async connectAsync(cancellationToken: CancellationToken): Promise<IPipeClientStream> {
        Broker.traceCategory.log(`Connecting to "${this._parameters.pipeName}"`);

        const connectFunc = this.connectCoreAsync.bind(this, cancellationToken);

        let result: IPipeClientStream | void | null = null;
        if (this._parameters.connectionFactory && !this._insideConnectionFactory) {
            this._insideConnectionFactory = true;
            try {
                result = await this._parameters.connectionFactory(connectFunc, cancellationToken);
            } finally {
                this._insideConnectionFactory = false;
            }
        }
        result = result || await connectFunc();

        this._newConnection = true;

        return result as any;
    }

    private processInboundAsync(event: MessageEvent): void {
        Broker._trace.log(`method:processInboundAsync, this._isDisposed === ${this._isDisposed}`);
        if (this._isDisposed) {
            Broker._trace.log(`method:processInboundAsync, this._isDisposed === ${this._isDisposed} => returning`);
            return;
        }

        if (event.message instanceof WireMessage.Request) {
            const tuple = SerializationPal.deserializeRequest(event.message);
            const callbackContext = new CallbackContext(
                tuple.brokerRequest,
                async brokerResponse => {
                    const wireResponse = SerializationPal.brokerResponseToWireResponse(brokerResponse, tuple.id);
                    try {
                        await event.messageStream.writeAsync(wireResponse, this._cts.token);
                    } catch (error) {
                        Broker.traceCategory.log(error);
                    }
                }
            );

            if (this._parameters.callback) {
                const callbackPromise = this.processCallbackAsync(this._parameters.callback, callbackContext).observe();
                this._inFlightCallbackPromises.push(callbackPromise);

                const unregister = this.unregisterInFlightCallback.bind(this, callbackPromise);
                callbackPromise.then(unregister, unregister);
            } else {
                callbackContext.respondAsync(new BrokerMessage.Response(
                    null,
                    new InvalidOperationError('Callbacks are not supported by this IpcClient.')
                ));
            }
        } else {
            /* istanbul ignore else */
            if (event.message instanceof WireMessage.Response) {
                const tuple = SerializationPal.deserializeResponse(event.message);
                this._table.signal(tuple.id, new Succeeded(tuple.brokerResponse));
            } else {
                throw new ArgumentError('Unexpected message type', 'message');
            }
        }
    }

    private unregisterInFlightCallback(callbackPromise: Promise<void>): void {
        const index = this._inFlightCallbackPromises.indexOf(callbackPromise);
        /* istanbul ignore else */
        if (index >= 0) {
            this._inFlightCallbackPromises.splice(index, 1);
        }
    }
    private async processCallbackAsync(callback: MethodContainer, callbackContext: CallbackContext): Promise<void> {
        const method = callback[callbackContext.request.methodName];
        if (method) {
            let maybeResult;
            let maybeError: Error | null = null;
            try {
                maybeResult = method.call(callback, ...callbackContext.request.args);
                if (maybeResult instanceof Promise) {
                    maybeResult = await maybeResult;
                }
            } catch (error) {
                maybeError = error;
            }

            const brokerResponse = !maybeError
                ? new BrokerMessage.Response(maybeResult, null)
                : new BrokerMessage.Response(null, maybeError);

            await callbackContext.respondAsync(brokerResponse);
        } else {
            await callbackContext.respondAsync(new BrokerMessage.Response(
                null,
                new InvalidOperationError(`Method not found.\r\nMethod name: ${callbackContext.request.methodName}`)
            ));
        }
    }

    private async ensureConnectedAsync(cancellationToken: CancellationToken): Promise<void> {
        if (!this._maybeMessageStream || !this._maybeMessageStream.isConnected) {
            if (this._maybeMessageStream) {
                await this._maybeMessageStream.disposeAsync();
                this._maybeMessageStream = null;
            }
            await this.getOrCreateMessageStreamAsync(cancellationToken);
        }
    }

    private async sendAsync(wireRequest: WireMessage.Request, cancellationToken: CancellationToken): Promise<void> {
        let fulfilled = false;
        while (!fulfilled) {
            Broker._trace.log(`method:sendAsync, loop: "while (!fulfilled)", this._cts.token.isCancellationRequested === ${this._cts.token.isCancellationRequested}`);
            this._cts.token.throwIfCancellationRequested();

            let messageStream: IMessageStream = null as any;
            try {
                messageStream = await this.getOrCreateMessageStreamAsync(cancellationToken);
            } catch (error) {
                Broker.traceCategory.log(`Broker.sendBufferAsync: Failed to obtain a StreamWrapper`);
                Broker.traceCategory.log(error);
                throw error;
            }

            try {
                if (this._parameters.beforeCall && !this._insideBeforeCall) {
                    this._insideBeforeCall = true;
                    try {
                        await this._parameters.beforeCall(wireRequest.MethodName, this._newConnection, cancellationToken);
                    } catch (error) {
                    }
                    this._newConnection = false;
                    this._insideBeforeCall = false;
                }
                await messageStream.writeAsync(wireRequest, cancellationToken);
                fulfilled = true;
            } catch (error) {
                await messageStream.disposeAsync();
                this._maybeMessageStream = null;
            }
        }
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            this._isDisposed = true;

            this._cts.cancel();

            Broker._trace.log(`method "disposeAsync": !!this._maybeMessageStream === ${!!this._maybeMessageStream}`);
            if (this._maybeMessageStream) {
                await this._maybeMessageStream.disposeAsync();
            }

            await Promise.all(this._inFlightCallbackPromises);

            this._table.dispose();
        }
    }
}
