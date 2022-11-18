import { IDisposable, assertArgument } from '..';
import { CancellationTokenSource } from '.';

export abstract class CancellationTokenRegistration implements IDisposable {
    public abstract dispose(): void;
}

/* @internal */
export class ProperCancellationTokenRegistration extends CancellationTokenRegistration {
    constructor(source: CancellationTokenSource, callback: () => void);
    constructor(
        private readonly _source: CancellationTokenSource,
        private readonly _callback: () => void,
    ) {
        super();
        assertArgument({ _source }, CancellationTokenSource);
        assertArgument({ _callback }, 'function');
    }

    public dispose(): void {
        this._source.unregister(this._callback);
    }
}

/* @internal */
export class EmptyCancellationTokenRegistration extends CancellationTokenRegistration {
    public static readonly instance = new EmptyCancellationTokenRegistration();

    private constructor() {
        super();
    }

    public dispose(): void {}
}
