/* @internal */
export class Memoizer<K = unknown, V = unknown> {
    private readonly _map = new Map<K, V>();

    public get(key: K, factory: (key: K) => V): V {
        let result = this._map.get(key);
        if (result === undefined) {
            result = factory(key);
            this._map.set(key, result);
        }
        return result;
    }
}
