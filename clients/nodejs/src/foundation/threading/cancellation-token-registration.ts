import { IDisposable } from '../disposable';
import { RegistrarCancellationToken } from './cancellation-token';
import { ArgumentNullError } from '../errors';

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

export class ProperCancellationTokenRegistration extends CancellationTokenRegistration {
    constructor(
        private readonly _cancellationToken: RegistrarCancellationToken,
        private readonly _callback: () => void
    ) {
        super();
        if (!_cancellationToken) { throw new ArgumentNullError('_cancellationToken'); }
        if (!_callback) { throw new ArgumentNullError('_callback'); }
    }
    public dispose(): void { this._cancellationToken.unregister(this._callback); }
}

export class NoneCancellationTokenRegistration extends CancellationTokenRegistration {
    public static readonly instance = new NoneCancellationTokenRegistration();
    private constructor() { super(); }
    public dispose(): void { /* */ }
}
