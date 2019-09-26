import { IDisposable } from '../disposable';
import { RegistrarCancellationToken } from './cancellation-token';

export abstract class CancellationTokenRegistration implements IDisposable {
    /* @internal */
    public static get none(): CancellationTokenRegistration { return NoneCancellationTokenRegistration.instance; }

    /* @internal */
    public static create(cancellationToken: RegistrarCancellationToken, callback: () => void): CancellationTokenRegistration {
        return new ProperCancellationTokenRegistration(cancellationToken, callback);
    }

    protected constructor() { /* */ }
    public abstract dispose(): void;
}

/* @internal */
export class ProperCancellationTokenRegistration extends CancellationTokenRegistration {
    constructor(
        private readonly _cancellationToken: RegistrarCancellationToken,
        private readonly _callback: () => void
    ) { super(); }
    public dispose(): void { this._cancellationToken.unregister(this._callback); }
}

/* @internal */
class NoneCancellationTokenRegistration extends CancellationTokenRegistration {
    public static readonly instance = new NoneCancellationTokenRegistration();
    private constructor() { super(); }
    public dispose(): void { /* */ }
}
