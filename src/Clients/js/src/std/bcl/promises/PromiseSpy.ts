import { PromiseStatus } from './PromiseStatus';

export interface PromiseSpy<T> {
    readonly promise: Promise<T>;
    readonly status: PromiseStatus;
    readonly result: T | undefined;
    readonly error: Error | undefined;
}
