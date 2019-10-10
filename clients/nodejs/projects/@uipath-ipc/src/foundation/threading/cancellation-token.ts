import { CancellationTokenRegistration, ProperCancellationTokenRegistration } from './cancellation-token-registration';
import { OperationCanceledError } from '../errors/operation-canceled-error';
import { AggregateError } from '../errors/aggregate-error';
import { ArgumentError } from '../errors/argument-error';
import { ArgumentNullError } from '@foundation/errors';

export abstract class CancellationToken {
    public static merge(...tokens: CancellationToken[]): CancellationToken {
        switch (tokens.length) {
            case 0:
                throw new ArgumentError('No tokens were supplied.', 'tokens');
            case 1:
                return tokens[0];
            default:
                return new LinkedCancellationToken(tokens);
        }
    }

    public static get none(): CancellationToken { return NoneCancellationToken.instance; }

    public abstract get canBeCanceled(): boolean;
    public abstract get isCancellationRequested(): boolean;

    public abstract register(callback: () => void): CancellationTokenRegistration;
    public throwIfCancellationRequested(): void {
        if (this.isCancellationRequested) {
            throw new OperationCanceledError();
        }
    }
}

/* @internal */
export abstract class RegistrarCancellationToken extends CancellationToken {
    private readonly _callbacks = new Array<() => void>();

    protected invokeCallbacks(throwOnFirstError: boolean): void {
        if (throwOnFirstError) {
            for (const callback of this._callbacks.splice(0)) {
                callback();
            }
        } else {
            const errors = new Array<Error>();
            for (const callback of this._callbacks.splice(0)) {
                try {
                    callback();
                } catch (error) {
                    errors.push(error);
                }
            }
            if (errors.length > 0) {
                throw new AggregateError(...errors);
            }
        }
    }

    public register(callback: () => void): CancellationTokenRegistration {
        if (!callback) { throw new ArgumentNullError('callback'); }
        if (this.isCancellationRequested) {
            callback();
            return CancellationTokenRegistration.none;
        } else {
            this._callbacks.push(callback);
            return CancellationTokenRegistration.create(this, callback);
        }
    }
    public unregister(callback: () => void): void {
        if (!callback) { throw new ArgumentNullError('callback'); }
        const index = this._callbacks.indexOf(callback);
        if (index >= 0) {
            this._callbacks.splice(index, 1);
        }
    }
}

/* @internal */
export class ProperCancellationToken extends RegistrarCancellationToken {
    private _isCancellationRequested = false;

    public get canBeCanceled(): boolean { return true; }
    public get isCancellationRequested(): boolean { return this._isCancellationRequested; }

    public cancel(throwOnFirstError: boolean): void {
        if (!this._isCancellationRequested) {
            this._isCancellationRequested = true;
            this.invokeCallbacks(throwOnFirstError);
        }
    }
}

/* @internal */
export class LinkedCancellationToken extends RegistrarCancellationToken {
    private readonly _registrations: CancellationTokenRegistration[];
    private _isCancellationRequested = false;

    public get canBeCanceled(): boolean { return true; }
    public get isCancellationRequested(): boolean { return this._isCancellationRequested; }

    constructor(tokens: CancellationToken[]) {
        super();
        const boundHandler = this.onCancellationRequested.bind(this);
        this._registrations = tokens.map(token => token.register(boundHandler));
    }

    private onCancellationRequested(): void {
        for (const registration of this._registrations.splice(0)) {
            registration.dispose();
        }

        this._isCancellationRequested = true;
        this.invokeCallbacks(false);
    }
}

class NoneCancellationToken extends CancellationToken {
    public static readonly instance = new NoneCancellationToken();

    public get canBeCanceled(): boolean { return false; }
    public get isCancellationRequested(): boolean { return false; }

    private constructor() { super(); }

    public register(callback: () => void): CancellationTokenRegistration { return CancellationTokenRegistration.none; }
    public throwIfCancellationRequested(): void { }
}
