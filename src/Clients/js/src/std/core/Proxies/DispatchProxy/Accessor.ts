/* @internal */
export class Accessor<TTarget, TKey extends string | number | symbol, TValue> {
    constructor(
        private readonly _target: TTarget,
        private readonly _key: TKey,
    ) {}

    public get value(): TValue | undefined {
        return this.pal[this._key];
    }

    public set value(value: TValue | undefined) {
        this.pal[this._key] = value;
    }

    private get pal(): {
        [key in TKey]: TValue | undefined;
    } {
        return this._target as any;
    }
}

/* @internal */
export module Accessor {
    export function of<TValue>(): Factory<TValue> {
        return cached;
    }

    export interface Factory<TValue> {
        from<TTarget, TKey extends string | number | symbol>(
            target: TTarget,
            key: TKey,
        ): Accessor<TTarget, TKey, TValue>;
    }

    const cached = <Factory<any>>{
        from<TTarget, TKey extends string | number | symbol>(
            target: TTarget,
            key: TKey,
        ): Accessor<TTarget, TKey, any> {
            return new Accessor<TTarget, TKey, any>(target, key);
        },
    };
}
