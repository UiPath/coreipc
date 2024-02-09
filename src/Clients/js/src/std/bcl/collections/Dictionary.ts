export class Dictionary<K, V> {
    public static create<K, V>(): Dictionary<K, V> {
        return new Dictionary<K, V>();
    }

    private readonly _map = new Map<K, V>();

    public getOrCreateValue(key: K, factory: (key: K) => V): V {
        if (this._map.has(key)) {
            return this._map.get(key)!;
        }

        let value = factory(key);
        this._map.set(key, value);
        return value;
    }
}
