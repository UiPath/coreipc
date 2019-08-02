import { CancellationTokenSource } from './cancellation-token-source';
import { Action0 } from '../delegates/delegates';
import { IDisposable, Disposable } from '../disposable/disposable';

export class CancellationToken {
    public static defaultIfFalsy(maybeCancellationToken: CancellationToken | null | undefined): CancellationToken {
        return maybeCancellationToken ? maybeCancellationToken : CancellationToken.default;
    }
    public static combine(...cancellationTokens: CancellationToken[]): CancellationToken {
        switch (cancellationTokens.length) {
            case 0: return CancellationToken.default;
            case 1: return cancellationTokens[0];
            default:
                return new CombinedCancellationToken(cancellationTokens);
        }
    }

    private static _default: CancellationToken;
    public static get default(): CancellationToken {
        if (!CancellationToken._default) {
            CancellationToken._default = CancellationTokenSource.default.token;
        }
        return CancellationToken._default;
    }

    public register(handler: Action0): IDisposable { throw null; }
    public get isCancellationRequested(): boolean { throw null; }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class SimpleCancellationToken extends CancellationToken {
    /* @internal */
    constructor(private readonly _source: CancellationTokenSource) {
        super();
        if (!(_source instanceof CancellationTokenSource)) {
            // tslint:disable-next-line: max-line-length
            throw new Error('Invalid operation: CancellationToken instances may only be created by CancellationTokenSource instances');
        }
    }

    public register(handler: Action0): IDisposable { return this._source.register(handler); }
    public get isCancellationRequested(): boolean { return this._source.isCancellationRequested; }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class CombinedCancellationToken extends CancellationToken {
    constructor(private readonly _cancellationTokens: CancellationToken[]) {
        super();
    }

    public register(handler: Action0): IDisposable {
        return Disposable.combine(...this._cancellationTokens.map((ct) => ct.register(handler)));
    }
    public get isCancellationRequested(): boolean {
        return this._cancellationTokens.reduce(
            (result, cursor) => result || cursor.isCancellationRequested,
            false as boolean);
    }
}
