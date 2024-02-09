import {
    CancellationToken,
    CancellationTokenRegistration,
    EmptyCancellationTokenRegistration,
} from '.';

import { assertArgument } from '..';

/* @internal */
export class EmptyCancellationToken extends CancellationToken {
    public static readonly instance = new EmptyCancellationToken();

    private constructor() {
        super();
    }

    public get canBeCanceled(): boolean {
        return false;
    }

    public get isCancellationRequested(): boolean {
        return false;
    }

    public throwIfCancellationRequested(): void {}

    public register(callback: () => void): CancellationTokenRegistration {
        assertArgument({ callback }, 'function');

        return EmptyCancellationTokenRegistration.instance;
    }

    public toString() {
        return 'CancellationToken.none';
    }
}
