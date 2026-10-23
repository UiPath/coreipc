import { AsyncLocalStorage } from 'async_hooks';
import { AmbientSlot } from '../../std';

/* @internal */
export class AsyncLocalSlot<T> implements AmbientSlot<T> {
    private readonly _storage = new AsyncLocalStorage<T | undefined>();

    public get(): T | undefined {
        return this._storage.getStore();
    }

    public run<R>(value: T | undefined, callback: () => R): R {
        return this._storage.run(value, callback);
    }
}
