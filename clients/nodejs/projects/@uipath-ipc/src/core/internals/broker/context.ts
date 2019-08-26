import * as BrokerMessage from './broker-message';
import { PromiseCompletionSource } from '../../../foundation/tasks/promise-completion-source';
import { ArgumentNullError } from '../../../foundation/errors/argument-null-error';
import { Result, Canceled } from '../../../foundation/result/result';
import { IDisposable } from '../../../foundation/disposable/disposable';

/* @internal */
export interface ICallContext {
    readonly id: string;
    readonly promise: Promise<BrokerMessage.Response>;
}

/* @internal */
class CallContext implements ICallContext {
    private readonly _pcs = new PromiseCompletionSource<BrokerMessage.Response>();
    public get promise(): Promise<BrokerMessage.Response> { return this._pcs.promise; }
    constructor(public readonly id: string) { }
    public set(result: Result<BrokerMessage.Response>) { this._pcs.set(result); }
}

/* @internal */
export class CallbackContext {
    constructor(
        public readonly request: BrokerMessage.Request,
        private readonly _respondAction: (response: BrokerMessage.Response) => Promise<void>
    ) { }
    public respondAsync(response: BrokerMessage.Response): Promise<void> {
        return this._respondAction(response);
    }
}

/* @internal */
export class CallContextTable implements IDisposable {
    private _nextId = 0;
    private _isDisposed = false;
    private readonly _contexts: { [id: string]: CallContext | undefined; } = {};

    public createContext(): ICallContext {
        const context = new CallContext(`${this._nextId++}`);
        this._contexts[context.id] = context;
        return context;
    }
    public signal(id: string, result: Result<BrokerMessage.Response>): void {
        if (!id) { throw new ArgumentNullError('id'); }

        const context = this._contexts[id];
        delete this._contexts[id];

        if (context) { context.set(result); }
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
                this.signal(id, Canceled.instance);
            }
        }
    }
}
