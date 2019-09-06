import * as BrokerMessage from './broker-message';
import * as WireMessage from './wire-message';
import { PipeClientStream, CancellationToken } from '../../..';
import { CallbackContext, CallContextTable } from './context';
import { SerializationPal } from './serialization-pal';
import { CancellationTokenSource } from '../../../foundation/tasks/cancellation-token-source';
import { IAsyncDisposable } from '../../../foundation/disposable/disposable';
import { ArgumentError } from '../../../foundation/errors/argument-error';
import { InvalidOperationError } from '../../../foundation/errors/invalid-operation-error';
import { StreamWrapper } from './stream-wrapper';
import { MessageEvent } from './message-event';
import { Succeeded } from '../../../foundation/outcome';
import { ILogicalSocketFactory } from '../../../foundation/pipes/logical-socket';
import { TimeSpan } from '../../../foundation/tasks/timespan';
import { ArgumentNullError } from '../../../foundation/errors/argument-null-error';

type IMethod = (this: IMethodContainer, ...args: any[]) => any;
interface IMethodContainer { readonly [methodName: string]: IMethod | undefined; }

/* @internal */
export interface IBroker extends IAsyncDisposable {
    sendReceiveAsync(brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response>;
}

/* @internal */
export class Broker implements IBroker, IAsyncDisposable {
    private readonly _inFlightCallbackPromises = new Array<Promise<void>>();
    private _isDisposed = false;
    private readonly _cts = new CancellationTokenSource();

    private _maybeWrapper: StreamWrapper | null = null;
    private readonly _calls = new CallContextTable();

    constructor(
        private readonly _factory: ILogicalSocketFactory,
        private readonly _pipeName: string,
        private readonly _connectTimeout: TimeSpan,
        private readonly _defaultCallTimeout: TimeSpan,
        private readonly _callback: IMethodContainer | undefined
    ) {
        if (!_factory) { throw new ArgumentNullError('_factory'); }
        if (!_pipeName) { throw new ArgumentNullError('_pipeName'); }
        if (!_connectTimeout) { throw new ArgumentNullError('_connectTimeout'); }
        if (!_defaultCallTimeout) { throw new ArgumentNullError('_defaultCallTimeout'); }
    }

    private async getOrCreateWrapperAsync(cancellationToken: CancellationToken): Promise<StreamWrapper> {
        if (!this._maybeWrapper) {
            const wrapper = new StreamWrapper(await this.connectAsync(cancellationToken));
            wrapper.messages.subscribe(this.processInboundAsync.bind(this));
            this._maybeWrapper = wrapper;
        }
        return this._maybeWrapper;
    }
    private async connectAsync(cancellationToken: CancellationToken): Promise<PipeClientStream> {
        return await await PipeClientStream.connectAsync(
            this._factory,
            this._pipeName,
            this._connectTimeout,
            cancellationToken);
    }

    private processInboundAsync(event: MessageEvent): void {

        // console.debug(`Got Inbound >> `, event);

        if (event.message instanceof WireMessage.Request) {
            const tuple = SerializationPal.deserializeRequest(event.message);
            const callbackContext = new CallbackContext(
                tuple.brokerRequest,
                async brokerResponse => {
                    const buffer = SerializationPal.serializeResponse(brokerResponse, tuple.id);
                    try {
                        await event.sender.stream.writeAsync(buffer, this._cts.token);
                    } catch (error) {
                        console.error(error);
                    }
                }
            );

            if (this._callback) {
                const callbackPromise = this.processCallbackAsync(this._callback, callbackContext);
                this._inFlightCallbackPromises.push(callbackPromise);
                callbackPromise.then(
                    _ => this.unregisterInFlightCallback(callbackPromise),
                    _ => this.unregisterInFlightCallback(callbackPromise)
                );
            } else {
                callbackContext.respondAsync(new BrokerMessage.Response(
                    null,
                    new InvalidOperationError('Callbacks are not supported by this IpcClient')
                ));
            }
        } else if (event.message instanceof WireMessage.Response) {
            const tuple = SerializationPal.deserializeResponse(event.message);
            this._calls.signal(tuple.id, new Succeeded(tuple.brokerResponse));
        } else {
            throw new ArgumentError('Unexpected message type', 'message');
        }
    }

    private unregisterInFlightCallback(callbackPromise: Promise<void>): void {
        const index = this._inFlightCallbackPromises.indexOf(callbackPromise);
        if (index >= 0) {
            this._inFlightCallbackPromises.splice(index, 1);
        }
    }
    private async processCallbackAsync(callback: IMethodContainer, callbackContext: CallbackContext): Promise<void> {
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

    public async sendReceiveAsync(brokerRequest: BrokerMessage.OutboundRequest): Promise<BrokerMessage.Response> {
        if (!brokerRequest) { throw new ArgumentNullError('brokerRequest'); }

        const tuple1 = SerializationPal.extract(brokerRequest, this._defaultCallTimeout);
        const context = this._calls.createContext(tuple1.cancellationToken);

        const buffer = SerializationPal.serializeRequest(context.id, brokerRequest.methodName, tuple1.serializedArgs, tuple1.timeoutSeconds, tuple1.cancellationToken);
        await this.sendBufferAsync(buffer, tuple1.cancellationToken);

        return await context.promise;
    }

    // TODO: Add timeout logic here
    private async sendBufferAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        let fulfilled = false;
        while (!fulfilled) {
            let wrapper: StreamWrapper = null as any;
            try {
                wrapper = await this.getOrCreateWrapperAsync(cancellationToken);
            } catch (error) {
                throw error;
            }

            try {
                await wrapper.stream.writeAsync(buffer, cancellationToken);
                fulfilled = true;
            } catch (error) {
                await wrapper.disposeAsync();
                this._maybeWrapper = null;
            }
        }
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            this._isDisposed = true;

            this._cts.cancel();

            if (this._maybeWrapper) {
                await this._maybeWrapper.disposeAsync();
            }

            for (const promise of this._inFlightCallbackPromises) {
                try {
                    await promise;
                } catch (error) {
                    console.error(error);
                }
            }

            this._calls.dispose();
        }
    }
}
