import { Subscription } from 'rxjs';
import { CancellationToken, IAsyncDisposable } from '@uipath/ipc-helpers';
import { IBroker, IBrokerWithCallbacks } from '../internals/broker';
import { InternalRequestMessage, InternalResponseMessage, IResponseError } from '../internals/internal-message';
import { CallbackContext } from '../internals/callback-context';
import { NamedPipeClientPal } from './named-pipe-client-pal';
import { IExecutor, IExecutionParams } from './executor';

type IMethod = (...args: any[]) => any;
interface IMethodContainer {
    [methodName: string]: IMethod;
}

export interface INamedPipeClient<TService> extends IAsyncDisposable {
    readonly proxy: TService;
}

export class NamedPipeClientBuilder {
    public static async createAsync<TService>(pipeName: string, servicePrototype: TService): Promise<INamedPipeClient<TService>> {
        const socket = await NamedPipeClientPal.connectAsync(pipeName);
        const broker = NamedPipeClientPal.createBroker(socket);

        return new NamedPipeClientImpl<TService, IBroker>(broker, servicePrototype);
    }
    public static async createWithCallbacksAsync<TService, TCallback>(
        pipeName: string,
        servicePrototype: TService,
        callbackService: TCallback): Promise<INamedPipeClient<TService>> {

        const socket = await NamedPipeClientPal.connectAsync(pipeName);
        const broker = NamedPipeClientPal.createBrokerWithCallbacks(socket);

        return new NamedPipeClientWithCallbacksImpl<TService, TCallback>(broker, servicePrototype, callbackService);
    }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class NamedPipeClientImpl<TService, TBroker extends IBroker> implements IAsyncDisposable, IExecutor, INamedPipeClient<TService> {
    public readonly proxy: TService;

    constructor(protected readonly _broker: TBroker, servicePrototype: TService) {
        this.proxy = NamedPipeClientPal.generateProxy<TService>(servicePrototype, this);
    }

    // Implementation of IExecutor -->
    public async executeAsync(executionParams: IExecutionParams): Promise<any> {
        const cancellationToken = executionParams.maybeCancellationToken || CancellationToken.default;

        let timeoutInSeconds = 0;
        if (executionParams.maybeMessage) {
            timeoutInSeconds = executionParams.maybeMessage.TimeoutInSeconds;
        }

        const request = new InternalRequestMessage(
            timeoutInSeconds,
            executionParams.methodName,
            executionParams.jsonArgs);
        const response = await this._broker.sendReceiveAsync(
            request,
            cancellationToken);

        return NamedPipeClientPal.unwrap(response);
    }
    // <--

    // Implementation of IAsyncDisposable -->
    public disposeAsync(): Promise<void> {
        return this._broker.disposeAsync();
    }
    // <--
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class NamedPipeClientWithCallbacksImpl<TService, TCallback>
    extends NamedPipeClientImpl<TService, IBrokerWithCallbacks>
    implements IAsyncDisposable, IExecutor, INamedPipeClient<TService> {

    private readonly _callbacksSubscription: Subscription;
    private readonly _callbackService: IMethodContainer;

    constructor(broker: IBrokerWithCallbacks, servicePrototype: TService, callbackService: TCallback) {
        super(broker, servicePrototype);
        this._callbackService = callbackService as any;
        this._callbacksSubscription = broker.callbacks.subscribe((context) => this.executeCallbackAsync(context));
    }

    private async executeCallbackAsync(context: CallbackContext): Promise<void> {
        const parameters = context.request.Parameters.map((json) => JSON.parse(json));
        const method = this._callbackService[context.request.MethodName] as IMethod;

        let response: InternalResponseMessage | null = null;
        try {
            let result = method.apply(this._callbackService, parameters);
            if (result instanceof Promise) {
                result = await result;
            }
            response = new InternalResponseMessage(JSON.stringify(result), null);
        } catch (ex) {
            if (!(ex instanceof Error)) {
                ex = new Error('Unknown error');
            }
            const error = ex as any as Error;
            const message = error.message;
            const type = 'Exception';
            const stackTrace = '';
            const responseError: IResponseError = {
                Message: message,
                Type: type,
                StackTrace: stackTrace,
                InnerError: null
            };
            response = new InternalResponseMessage(null, responseError);
        }

        await context.respondAsync(response, CancellationToken.default);
    }

    /* @override */
    public async disposeAsync(): Promise<void> {
        await super.disposeAsync();
        this._callbacksSubscription.unsubscribe();
    }
}
