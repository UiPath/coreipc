import * as BrokerMessage from './broker-message';
import * as Outcome from '@foundation/utils';

import { PromiseCompletionSource } from '../../foundation/threading/promise-completion-source';
import { ArgumentNullError } from '../../foundation/errors/argument-null-error';
import { IDisposable, IAsyncDisposable } from '../../foundation/disposable';
import { ObjectDisposedError } from '../../foundation/errors/object-disposed-error';
import { CancellationToken } from '../..';
import { CancellationTokenRegistration } from '../../foundation/threading/cancellation-token-registration';

/* @internal */
export interface ICallContext extends IDisposable {
    readonly id: string;
    readonly promise: Promise<BrokerMessage.Response>;
}

/* @internal */
export class CallContext implements ICallContext {
    private readonly _pcs = new PromiseCompletionSource<BrokerMessage.Response>();
    private readonly _ctreg: CancellationTokenRegistration;

    public get promise(): Promise<BrokerMessage.Response> {
        return this._pcs.promise;
    }

    constructor(public readonly id: string, cancellationToken: CancellationToken, private readonly _maybeDisposable?: IDisposable) {
        if (!id) { throw new ArgumentNullError('id'); }
        if (!cancellationToken) { throw new ArgumentNullError('cancellationToken'); }

        this._ctreg = cancellationToken.register(() => {
            this.trySet(new Outcome.Canceled());
        });
    }

    public trySet(outcome: Outcome.AnyOutcome<BrokerMessage.Response>) {
        if (!outcome) { throw new ArgumentNullError('outcome'); }

        this._ctreg.dispose();
        this._pcs.trySet(outcome);
    }

    public dispose(): void {
        if (this._maybeDisposable) {
            this._maybeDisposable.dispose();
        }
    }
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

/* @internal */
export class CallContextTable implements IDisposable {
    private _nextId = 0;
    private _isDisposed = false;
    private readonly _contexts: { [id: string]: CallContext | undefined; } = {};

    public createContext(cancellationToken: CancellationToken, maybeDisposable?: IDisposable): ICallContext {
        if (!cancellationToken) { throw new ArgumentNullError(`cancellationToken`); }
        if (this._isDisposed) { throw new ObjectDisposedError('CallContextTable'); }

        const context = new CallContext(`${this._nextId++}`, cancellationToken, maybeDisposable);
        this._contexts[context.id] = context;
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
