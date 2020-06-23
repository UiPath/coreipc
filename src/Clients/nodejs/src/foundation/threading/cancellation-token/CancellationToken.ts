import {
    EmptyCancellationToken,
    CancellationTokenRegistration,
} from '.';

export abstract class CancellationToken {
    public static get none(): CancellationToken { return EmptyCancellationToken.instance; }

    protected constructor() { }

    public abstract get canBeCanceled(): boolean;
    public abstract get isCancellationRequested(): boolean;
    public abstract throwIfCancellationRequested(): void | never;
    public abstract register(callback: () => void): CancellationTokenRegistration;
}
