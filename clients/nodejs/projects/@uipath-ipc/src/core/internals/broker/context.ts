import * as BrokerMessage from './broker-message';
import { PromiseCompletionSource } from '../../../foundation/tasks/promise-completion-source';
import { ArgumentNullError } from '../../../foundation/errors/argument-null-error';
import * as Outcome from '../../../foundation/outcome';
import { IDisposable } from '../../../foundation/disposable/disposable';
import { ObjectDisposedError } from '../../../foundation/errors/object-disposed-error';

/* @internal */
export interface ICallContext {
    readonly id: string;
    readonly promise: Promise<BrokerMessage.Response>;
}

/* @internal */
export class CallContext implements ICallContext {
    private readonly _pcs = new PromiseCompletionSource<BrokerMessage.Response>();
    public get promise(): Promise<BrokerMessage.Response> { return this._pcs.promise; }
    constructor(public readonly id: string) {
        if (!id) { throw new ArgumentNullError('id'); }
    }
    public set(outcome: Outcome.Any<BrokerMessage.Response>) { this._pcs.set(outcome); }
}

/* @internal */
export class CallbackContext {
    constructor(
        public readonly request: BrokerMessage.Request,
        private readonly _respondAction: (response: BrokerMessage.Response) => Promise<void>
    ) {
        if (!request) { throw new ArgumentNullError('request'); }
        if (!_respondAction) { throw new ArgumentNullError('_respondAction'); }
    }
    public async respondAsync(response: BrokerMessage.Response): Promise<void> {
        return await this._respondAction(response);
    }
}

/* @internal */
export class CallContextTable implements IDisposable {
    private _nextId = 0;
    private _isDisposed = false;
    private readonly _contexts: { [id: string]: CallContext | undefined; } = {};

    public createContext(): ICallContext {
        if (this._isDisposed) { throw new ObjectDisposedError('CallContextTable'); }
        const context = new CallContext(`${this._nextId++}`);
        this._contexts[context.id] = context;
        return context;
    }
    public signal(id: string, outcome: Outcome.Any<BrokerMessage.Response>): void {
        if (!id) { throw new ArgumentNullError('id'); }
        if (!outcome) { throw new ArgumentNullError('outcome'); }
        if (this._isDisposed) { throw new ObjectDisposedError('CallContextTable'); }

        this.signalUnchecked(id, outcome);
    }

    private signalUnchecked(id: string, outcome: Outcome.Any<BrokerMessage.Response>): void {
        const context = this._contexts[id];
        delete this._contexts[id];

        if (context) { context.set(outcome); }
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
