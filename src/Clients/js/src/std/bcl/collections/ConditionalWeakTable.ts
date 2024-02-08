import { assertArgument } from "../helpers";

export class ConditionalWeakTable<K, V> {
    private readonly _symbol = Symbol();

    public getOrCreateValue(key: K, factory: (key: K) => V): V {
        assertArgument({ key }, 'object', 'function');
        assertArgument({ factory }, 'function');

        return (key as any)[this._symbol] ??= factory(key);
    }
}
