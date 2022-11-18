import {
    assertArgument,
    ArgumentOutOfRangeError,
    ObjectDisposedError,
    AggregateError,
    IDisposable,
    AggregateDisposable,
    TimeSpan,
    ArgumentError,
    UnknownError,
} from '..';

import {
    CancellationToken,
    CancellationTokenRegistration,
    RandomCancellationToken,
    ProperCancellationTokenRegistration,
    LinkedCancellationTokenSource,
} from '.';

export class CancellationTokenSource implements IDisposable {
    public static createLinkedTokenSource(...tokens: CancellationToken[]): CancellationTokenSource {
        if (tokens.length === 0) {
            throw new ArgumentError('No tokens were supplied.');
        }
        if (tokens.filter((x) => !(x instanceof CancellationToken)).length > 0) {
            throw new ArgumentError(
                'Some supplied arguments were not instances of CancellationToken.',
            );
        }

        function onCancellation(): void {
            disposable.dispose();
            result.cancel();
        }

        const disposable = new AggregateDisposable(
            ...tokens.map((token) => token.register(onCancellation)),
        );

        const result = new LinkedCancellationTokenSource(disposable);
        return result;
    }

    private readonly _token: RandomCancellationToken = new RandomCancellationToken(this);

    protected _isDisposed = false;
    private _isCancellationRequested = false;
    private _nodeJsTimeout: NodeJS.Timeout | null = null;

    private readonly _callbacks = new Array<() => void>();

    constructor();
    constructor(millisecondsDelay: number);
    constructor(delay: TimeSpan);
    constructor(arg0?: number | TimeSpan) {
        const paramName: string = typeof arg0 === 'number' ? 'millisecondsDelay' : 'delay';
        if (arg0 != null) {
            arg0 = TimeSpan.toTimeSpan(arg0);
        }

        if (arg0 && !arg0.isInfinite) {
            if (arg0.isNegative) {
                throw new ArgumentOutOfRangeError(
                    paramName,
                    'Specified argument represented a negative interval.',
                );
            }

            this.cancelAfterUnchecked(arg0);
        }
    }

    public get token(): CancellationToken {
        return this._token;
    }

    public cancel(throwOnFirstError: boolean = false): void {
        assertArgument({ throwOnFirstError }, 'boolean');

        this.assertNotDisposed();
        if (this._isCancellationRequested) {
            return;
        }

        this.cancelUnchecked(throwOnFirstError);
    }

    public cancelAfter(millisecondsDelay: number): void;
    public cancelAfter(delay: TimeSpan): void;
    public cancelAfter(arg0: number | TimeSpan): void {
        assertArgument({ arg0 }, 'number', TimeSpan);

        this.assertNotDisposed();

        const paramName = typeof arg0 === 'number' ? 'millisecondsDelay' : 'delay';
        arg0 = TimeSpan.toTimeSpan(arg0);
        if (arg0.isNegative && !arg0.isInfinite) {
            throw new ArgumentOutOfRangeError(paramName);
        }
        if (this._isCancellationRequested) {
            return;
        }

        this.cancelAfterUnchecked(arg0);
    }

    public dispose(): void {
        if (this._isDisposed) {
            return;
        }
        this._isDisposed = true;
        this.ensureTimeoutCleared();
    }

    /* @internal */ public registerUnchecked(callback: () => void): CancellationTokenRegistration {
        if (this._isCancellationRequested) {
            callback();
        } else {
            this._callbacks.push(callback);
        }
        return new ProperCancellationTokenRegistration(this, callback);
    }

    /* @internal */ public unregister(callback: () => void): void {
        const index = this._callbacks.indexOf(callback);
        if (index >= 0) {
            this._callbacks.splice(index, 1);
        }
    }

    /* @internal */ public get isCancellationRequested(): boolean {
        return this._isCancellationRequested;
    }

    private cancelAfterUnchecked(delay: TimeSpan): void {
        this.ensureTimeoutCleared();
        this._nodeJsTimeout = setTimeout(this.onTimeout, delay.totalMilliseconds);
    }

    private readonly onTimeout = () => {
        try {
            this.cancelUnchecked(false);
        } catch (error) {}
    };

    private cancelUnchecked(throwOnFirstError: boolean): void {
        this.ensureTimeoutCleared();
        this._isCancellationRequested = true;
        const callbacks = this._callbacks.splice(0);

        if (throwOnFirstError) {
            for (const callback of callbacks) {
                callback();
            }
        } else {
            const errors = new Array<Error>();
            for (const callback of callbacks) {
                try {
                    callback();
                } catch (error) {
                    errors.push(UnknownError.ensureError(error));
                }
            }
            if (errors.length > 0) {
                throw new AggregateError(undefined, ...errors);
            }
        }
    }

    private assertNotDisposed(): void {
        if (this._isDisposed) {
            throw new ObjectDisposedError(
                undefined,
                'The CancellationTokenSource has been disposed.',
            );
        }
    }

    private ensureTimeoutCleared(): void {
        if (this._nodeJsTimeout) {
            clearTimeout(this._nodeJsTimeout);
            this._nodeJsTimeout = null;
        }
    }
}
