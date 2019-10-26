import * as BrokerMessage from './broker-message';
import * as WireMessage from './wire-message';
import * as Outcome from '../../foundation/utils';

import { PromiseCompletionSource } from '../../foundation/threading/promise-completion-source';
import { ArgumentNullError } from '../../foundation/errors/argument-null-error';
import { IDisposable, IAsyncDisposable } from '../../foundation/disposable';
import { ObjectDisposedError } from '../../foundation/errors/object-disposed-error';
import { CancellationToken } from '../..';
import { CancellationTokenRegistration } from '../../foundation/threading/cancellation-token-registration';
import { ArgumentError } from '@uipath/ipc/foundation/errors';

/* @internal */
export interface ICallContext extends IDisposable {
    readonly wireRequest: WireMessage.Request;
    readonly cancellationToken: CancellationToken;
    readonly promise: Promise<BrokerMessage.Response>;
}

/* @internal */
export class CallContext implements ICallContext {
    private readonly _pcs = new PromiseCompletionSource<BrokerMessage.Response>();
    private readonly _ctreg: CancellationTokenRegistration;

    public get cancellationToken(): CancellationToken { return this._data.cancellationToken; }
    public get wireRequest(): WireMessage.Request { return this._data.wireRequest; }

    public get promise(): Promise<BrokerMessage.Response> { return this._pcs.promise; }

    constructor(
        private readonly _data: ICallContextData
    ) {
        if (!_data) { throw new ArgumentNullError('_data'); }
        if (!_data.cancellationToken) { throw new ArgumentError('Expecting a non-null, non-undefined CancellationToken on the provided ICallContextData.', '_data'); }
        if (!_data.wireRequest) { throw new ArgumentError('Expecting a non-null, non-undefined WireMessage.Request on the provided ICallContextData.', '_data'); }

        this._ctreg = _data.cancellationToken.register(() => {
            this.trySet(new Outcome.Canceled());
        });
    }

    public trySet(outcome: Outcome.AnyOutcome<BrokerMessage.Response>) {
        if (!outcome) { throw new ArgumentNullError('outcome'); }

        this._ctreg.dispose();
        this._pcs.trySet(outcome);
    }

    public dispose(): void { this._data.dispose(); }
}

/* @internal */
export class CallbackContext {
    constructor(
        public readonly request: BrokerMessage.InboundRequest,
        private readonly _respondAction: (response: BrokerMessage.Response) => Promise<void>
    ) {
        if (!request) { throw new ArgumentNullError('request'); }
        if (!_respondAction) { throw new ArgumentNullError('_respondAction'); }
    }
    public async respondAsync(response: BrokerMessage.Response): Promise<void> {
        if (!response) { throw new ArgumentNullError('response'); }
        return await this._respondAction(response);
    }
}

export type ICallContextDataFactory = (id: string) => ICallContextData;

/* @internal */
export interface ICallContextData extends IDisposable {
    readonly cancellationToken: CancellationToken;
    readonly wireRequest: WireMessage.Request;
}

/* @internal */
export class CallContextTable implements IDisposable {
    private _nextId = 0;
    private _isDisposed = false;
    private readonly _contexts: { [id: string]: CallContext | undefined; } = {};

    public createContext(factory: ICallContextDataFactory): ICallContext {
        if (!factory) { throw new ArgumentNullError(`factory`); }
        if (this._isDisposed) { throw new ObjectDisposedError('CallContextTable'); }

        const context = new CallContext(factory(`${this._nextId++}`));

        this._contexts[context.wireRequest.Id] = context;
        return context;
    }
    public signal(id: string, outcome: Outcome.AnyOutcome<BrokerMessage.Response>): void {
        if (!id) { throw new ArgumentNullError('id'); }
        if (!outcome) { throw new ArgumentNullError('outcome'); }
        if (this._isDisposed) { throw new ObjectDisposedError('CallContextTable'); }

        this.signalUnchecked(id, outcome);
    }

    private signalUnchecked(id: string, outcome: Outcome.AnyOutcome<BrokerMessage.Response>): void {
        const context = this._contexts[id];
        delete this._contexts[id];

        if (context) {
            context.trySet(outcome);
            context.dispose();
        }
    }

    public dispose(): void {
        if (!this._isDisposed) {
            this._isDisposed = true;

            function isString(x: string | number | symbol): x is string {
                return typeof x === 'string';
            }

            const ids = Reflect
                .ownKeys(this._contexts)
                .filter(isString);

            for (const id of ids) {
                this.signalUnchecked(id, new Outcome.Canceled());
            }
        }
    }
}
