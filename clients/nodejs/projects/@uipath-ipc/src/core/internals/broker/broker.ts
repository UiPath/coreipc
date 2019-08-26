import * as BrokerMessage from './broker-message';
import * as WireMessage from './wire-message';
import { PipeClientStream, CancellationToken } from '../../..';
import { CallbackContext, CallContextTable } from './context';
import { SerializationHelper } from './serialization-helper';
import { PipeBrokenError } from '../../../foundation/errors/pipe/pipe-broken-error';
import { CancellationTokenSource } from '../../../foundation/tasks/cancellation-token-source';
import { IAsyncDisposable } from '../../../foundation/disposable/disposable';
import { ArgumentError } from '../../../foundation/errors/argument-error';
import { InvalidOperationError } from '../../../foundation/errors/invalid-operation-error';
import { Emitter } from './emitter';
import { MessageEvent } from './message-event';
import { Succeeded } from '../../../foundation/result/result';

type IMethod = (this: IMethodContainer, ...args: any[]) => any;
interface IMethodContainer { readonly [methodName: string]: IMethod | undefined; }

/* @internal */
export class Broker implements IAsyncDisposable {
    private readonly _inFlightCallbackPromises = new Array<Promise<void>>();
    private _isDisposed = false;
    private readonly _cts = new CancellationTokenSource();

    private _maybeEmitter: Emitter | null = null;
    private readonly _calls = new CallContextTable();

    constructor(
        private readonly _pipeName: string,
        private readonly _connectTimeoutMilliseconds: number,
        private readonly _defaultCallTimeoutMilliseconds: number,
        private readonly _callback: IMethodContainer | undefined
    ) { }

    private async getEmitterAsync(cancellationToken: CancellationToken): Promise<Emitter> {
        if (!this._maybeEmitter) {
            const emitter = new Emitter(await this.connectAsync(cancellationToken));
            emitter.messages.subscribe(this.processInboundAsync.bind(this));
            this._maybeEmitter = emitter;
        }
        return this._maybeEmitter;
    }
    private async connectAsync(cancellationToken: CancellationToken): Promise<PipeClientStream> {
        return await await PipeClientStream.connectAsync(
            this._pipeName,
            this._connectTimeoutMilliseconds,
            cancellationToken);
    }

    private processInboundAsync(event: MessageEvent): void {
        if (event.message instanceof WireMessage.Request) {
            const tuple = SerializationHelper.deserializeRequest(event.message);
            const callbackContext = new CallbackContext(
                tuple.brokerRequest,
                async brokerResponse => {
                    const buffer = SerializationHelper.serializeResponse(brokerResponse, tuple.id);
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
            const tuple = SerializationHelper.deserializeResponse(event.message);
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

    public async sendReceiveAsync(brokerRequest: BrokerMessage.Request): Promise<BrokerMessage.Response> {
        const context = this._calls.createContext();
        await this.sendRequestAsync(brokerRequest, context.id);
        return await context.promise;
    }
    private async sendRequestAsync(brokerRequest: BrokerMessage.Request, id: string): Promise<void> {
        const obj = SerializationHelper.serializeRequest(brokerRequest, id, this._defaultCallTimeoutMilliseconds);
        await this.sendBufferAsync(obj.buffer, obj.cancellationToken);
    }

    // TODO: Add timeout logic here
    private async sendBufferAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        let fulfilled = false;
        while (!fulfilled) {
            let emitter: Emitter = null as any;
            try {
                emitter = await this.getEmitterAsync(cancellationToken);
            } catch (error) {
                throw error;
            }

            try {
                await emitter.stream.writeAsync(buffer, cancellationToken);
                fulfilled = true;
            } catch (error) {
                await emitter.disposeAsync();
                this._maybeEmitter = null;
            }
        }
    }

    public async disposeAsync(): Promise<void> {
        if (!this._isDisposed) {
            this._isDisposed = true;

            this._cts.cancel();

            if (this._maybeEmitter) {

                await this._maybeEmitter.disposeAsync();
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
