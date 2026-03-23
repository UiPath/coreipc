export class Dictionary<K, V> {
    public static create<K, V>(): Dictionary<K, V> {
        return new Dictionary<K, V>();
    }

    private readonly _map = new Map<K, V>();

    public get(key: K): V | undefined {
        return this._map.get(key);
    }

    public getOrCreateValue(key: K, factory: (key: K) => V): V {
        if (this._map.has(key)) {
            return this._map.get(key)!;
        }

        let value = factory(key);
        this._map.set(key, value);
        return value;
    }

    public delete(key: K): boolean {
        return this._map.delete(key);
    }

    public values(): IterableIterator<V> {
        return this._map.values();
    }

    public clear(): void {
        this._map.clear();
    }
}
