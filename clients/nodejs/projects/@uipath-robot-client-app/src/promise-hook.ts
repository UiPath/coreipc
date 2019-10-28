export class PromiseHook<T> {
    private static _oldPromise: PromiseConstructor;
    private static readonly _remaining: { [id: number]: PromiseHook<unknown> } = {};
    public static *remaining(): Iterable<PromiseHook<unknown>> {
        for (const x of Reflect.ownKeys(PromiseHook._remaining)) {
            if (typeof x === 'number') {
                yield PromiseHook._remaining[x];
            }
        }
    }

    public static install(): void {
        PromiseHook._oldPromise = Promise;
        global['Promise'] = PromiseHook;
    }

    private static sequence = 0;
    private readonly _realPromise: any;
    public get realPromise(): any { return this._realPromise; }
    private _realResolve: any;
    private _realReject: any;
    private readonly id = PromiseHook.sequence++;

    constructor(executor: (resolve: (value?: T | PromiseLike<T>) => void, reject: (reason?: any) => void) => void) {
        PromiseHook._remaining[this.id] = this;

        this._realPromise = new PromiseHook._oldPromise<T>((_resolve, _reject) => {
            this._realResolve = _resolve;
            this._realReject = _reject;
            executor(
                this.resolve.bind(this) as any,
                this.reject.bind(this)
            );
        });
    }

    private resolve(value: T): void {
        this._realResolve(value);

        const found = PromiseHook._remaining[this.id];
        if (found) {
            delete PromiseHook._remaining[this.id];
        }
    }
    private reject(reason?: any): void {
        this._realReject(reason);

        const found = PromiseHook._remaining[this.id];
        if (found) {
            delete PromiseHook._remaining[this.id];
        }
    }

    public observe(): this {
        this._realPromise.observe();
        return this;
    }

    public traceError(): any {
        return this._realPromise.traceError();
    }

    public then(...args: any[]): void {
        this._realPromise.then(...args);
    }

    public static all(...args: any[]): any {
        return (PromiseHook._oldPromise as any).all(...args);
    }
}
