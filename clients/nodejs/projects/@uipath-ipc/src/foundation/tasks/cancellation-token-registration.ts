import { IDisposable } from '../disposable/disposable';
import { AbstractMemberError } from '../errors/abstract-member-error';
import { CancellationToken } from './cancellation-token';

export class CancellationTokenRegistration implements IDisposable {
    /* @internal */
    public static get none(): CancellationTokenRegistration { return NoneCancellationTokenRegistration.instance; }

    /* @internal */
    public static create(cancellationToken: CancellationToken, callback: () => void): CancellationTokenRegistration {
        return new ProperCancellationTokenRegistration(cancellationToken, callback);
    }

    protected constructor() { /* */ }
    public dispose(): void { throw new AbstractMemberError(); }
}

/* @internal */
export class ProperCancellationTokenRegistration extends CancellationTokenRegistration {
    constructor(
        private readonly _cancellationToken: CancellationToken,
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
