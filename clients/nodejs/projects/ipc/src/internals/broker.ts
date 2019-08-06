import { CallbackContext } from './callback-context';
import { Observable, ReplaySubject, Subject } from 'rxjs';

import {
    IAsyncDisposable,
    CancellationTokenSource,
    CancellationToken,
    PromiseCompletionSource,
    ArgumentNullError,
    PromiseHelper
} from '@uipath/ipc-helpers';

import { IChannel } from './channel';
import {
    InternalMessage,
    InternalRequestMessage,
    InternalResponseMessage
} from './internal-message';

/* @internal */
export interface IBroker extends IAsyncDisposable {
    sendReceiveAsync(request: InternalRequestMessage, cancellationToken: CancellationToken): Promise<InternalResponseMessage>;
}

/* @internal */
export interface IBrokerWithCallbacks extends IBroker {
    readonly callbacks: Observable<CallbackContext>;
}

interface IStringKeyDictionary<T> {
    [key: string]: T;
}
interface IProcessInfo {
    readonly message: InternalMessage;
    readonly promise: Promise<void>;
}
type ICompletionDictionary = IStringKeyDictionary<PromiseCompletionSource<InternalResponseMessage>>;
type IProcessDictionary = IStringKeyDictionary<IProcessInfo>;

class Sequence {
    private _value = 0;
    public get next(): string { return `${this._value++}`; }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class Broker implements IBroker {
    private readonly _cancellationSource = new CancellationTokenSource();
    private readonly _loop: Promise<void>;
    private readonly _completions: ICompletionDictionary = {};
    private readonly _processes: IProcessDictionary = {};
    private readonly _requestId = new Sequence();
    private readonly _processId = new Sequence();
    private readonly _errors = new Subject<Error>();

    protected get cancellationToken(): CancellationToken { return this._cancellationSource.token; }
    public get errors(): Observable<Error> { return this._errors; }

    constructor(protected readonly _channel: IChannel) {
        if (!_channel) {
            throw new ArgumentNullError('channel');
        }
        this._loop = this.loopAsync();
    }

    private async loopAsync(): Promise<void> {
        let didReceiveNull = false;

        while (!didReceiveNull && !this.cancellationToken.isCancellationRequested) {
            const message = await this.tryReadMessageAsync();

            if (message) {
                const promise = this.processMessageAsync(message);

                /* no await */
                this.registerAndFollowUpAsync(message, promise);
            } else {
                didReceiveNull = true;
            }
        }
    }

    private logError(error: Error): void { this._errors.next(error); }

    private async registerAndFollowUpAsync(message: InternalMessage, promise: Promise<void>): Promise<void> {
        const id = this._processId.next;
        this._processes[id] = { message, promise };

        try {
            await promise;
        } catch (error) {
            this.logError(error);
        }

        delete this._processes[id];
    }
    // tslint:disable-next-line: member-ordering
    protected async processMessageAsync(message: InternalMessage): Promise<void> {
        if (message instanceof InternalResponseMessage) {
            await this.processResponseAsync(message);
        } else {
            throw new Error(`Not supported message type (${message})`);
        }
    }
    private async processResponseAsync(response: InternalResponseMessage): Promise<void> {
        this._completions[response.RequestId].setResult(response);
        delete this._completions[response.RequestId];
    }

    private async tryReadMessageAsync(): Promise<InternalMessage | null> {
        let result: InternalMessage | null;
        try {
            result = await InternalMessage.readAsync(this._channel, this.cancellationToken);
        } catch (error) {
            this.logError(error);
            result = null;
        }
        return result;
    }

    // tslint:disable-next-line: member-ordering
    public async sendReceiveAsync(request: InternalRequestMessage, cancellationToken: CancellationToken): Promise<InternalResponseMessage> {
        request.Id = this._requestId.next;
        const pcs = new PromiseCompletionSource<InternalResponseMessage>();
        this._completions[request.Id] = pcs;

        await request.writeWithEnvelopeAsync(this._channel, cancellationToken);
        const response = await pcs.promise;

        return response;
    }

    // tslint:disable-next-line: member-ordering
    public async disposeAsync(): Promise<void> {
        try {
            this._cancellationSource.cancel();
        } catch (error) {
            this._errors.next(error);
        }

        await this._loop;

        for (const requestId in this._completions) {
            if (typeof requestId === 'string') {
                this._completions[requestId].trySetCanceled();
            }
        }

        const promises = Object
            .keys(this._processes)
            .map(key => this._processes[key])
            .map((process: IProcessInfo) => process.promise);

        await PromiseHelper.whenAll(...promises);

        this._channel.dispose();
        this._errors.complete();
    }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class BrokerWithCallbacks extends Broker implements IBrokerWithCallbacks {
    private readonly _callbacks = new ReplaySubject<CallbackContext>();
    public get callbacks(): Observable<CallbackContext> { return this._callbacks; }

    constructor(_channel: IChannel) {
        super(_channel);
    }

    protected async processMessageAsync(message: InternalMessage): Promise<void> {
        if (message instanceof InternalRequestMessage) {
            const callbackContext = new CallbackContext(
                message,
                this.cancellationToken,
                this._channel);

            this._callbacks.next(callbackContext);
        } else {
            await super.processMessageAsync(message);
        }
    }

    // tslint:disable-next-line: member-ordering
    public async disposeAsync(): Promise<void> {
        await super.disposeAsync();
        this._callbacks.complete();
    }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
// export class OldBroker implements IBroker {
//     private readonly _cts = new CancellationTokenSource();
//     private readonly _loop: Promise<void>;
//     private readonly _callbacks = new ReplaySubject<CallbackContext>();
//     private readonly _completionTable: ICompletionDictionary = {};
//     private _sequence = 0;
//     private get nextRequestId(): string { return `${this._sequence++}`; }

//     public get callbacks(): Observable<CallbackContext> { return this._callbacks; }

//     constructor(private readonly _channel: IChannel) {
//         if (!_channel) {
//             throw new ArgumentNullError('_channel');
//         }
//         this._loop = this.loopAsync();
//     }

//     public async sendReceiveAsync(request: InternalRequestMessage, cancellationToken: CancellationToken): Promise<InternalResponseMessage> {
//         request.Id = this.nextRequestId;
//         const pcs = new PromiseCompletionSource<InternalResponseMessage>();
//         this._completionTable[request.Id] = pcs;

//         await request.writeWithEnvelopeAsync(this._channel, cancellationToken);
//         const response = await pcs.promise;

//         return response;
//     }

//     public async disposeAsync(): Promise<void> {
//         this._cts.cancel();
//         await this._loop;
//         for (const requestId in this._completionTable) {
//             if (typeof requestId === 'string') {
//                 this._completionTable[requestId].trySetCanceled();
//             }
//         }
//     }

//     private async loopAsync(): Promise<void> {
//         const token = this._cts.token;

//         while (!token.isCancellationRequested) {
//             const message = await Broker.tryReadMessageAsync(this._channel, token);

//             if (message instanceof InternalRequestMessage) {
//                 this._callbacks.next(new CallbackContext(message, this._channel));

//             } else if (message instanceof InternalResponseMessage) {
//                 this._completionTable[message.RequestId].setResult(message);
//                 delete this._completionTable[message.RequestId];

//             } else {
//                 return;
//             }
//         }
//     }

//     // tslint:disable-next-line: member-ordering
//     private static async tryReadMessageAsync(channel: IChannel, token: CancellationToken): Promise<InternalMessage | null> {
//         try {
//             return await InternalMessage.readAsync(channel, token);
//         } catch (ex) {
//             return null;
//         }
//     }
// }
