// tslint:disable: only-arrow-functions

import { CancellationToken } from './cancellation-token';
import { CancellationTokenRegistration } from './cancellation-token-registration';
import { PromiseCompletionSource } from './promise-completion-source';
import { TimeSpan } from './timespan';
import { ArgumentError } from '../errors/argument-error';
import { OperationCanceledError } from '../errors/operation-canceled-error';

class PromisePal {
    public static readonly completedPromise = new Promise<void>(resolve => resolve(undefined));

    public static fromResult<T>(result: T): Promise<T> {
        return new Promise<T>((resolve, _) => resolve(result));
    }
    public static fromError<T>(error: Error): Promise<T> {
        return new Promise<T>((_, reject) => reject(error));
    }
    public static fromCanceled<T>(): Promise<T> {
        return new Promise<T>((_, reject) => reject(new OperationCanceledError()));
    }

    public static delay(timespan: TimeSpan, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        if (timespan.isNegative) { throw new ArgumentError('Cannot delay for a negative timespan.', 'timespan'); }
        const pcs = new PromiseCompletionSource<void>();

        const timeoutId = setTimeout(() => {
            pcs.setResult(undefined);
            if (maybeRegistration) {
                maybeRegistration.dispose();
            }
        }, timespan.totalMilliseconds);
        const maybeRegistration: CancellationTokenRegistration | null = cancellationToken.canBeCanceled
            ? cancellationToken.register(() => {
                clearTimeout(timeoutId);
                pcs.setCanceled();
            })
            : null;

        return pcs.promise;
    }
    public static yield(): Promise<void> {
        const pcs = new PromiseCompletionSource<void>();
        setTimeout(pcs.setResult.bind(pcs), 0);
        return pcs.promise;
    }
}

export { };

declare global {
    interface PromiseConstructor {
        delay(delay: TimeSpan, cancellationToken?: CancellationToken): Promise<void>;
        delay(millisecondsDelay: number, cancellationToken?: CancellationToken): Promise<void>;

        readonly completedPromise: Promise<void>;
        fromResult<T>(result: T): Promise<T>;
        fromError<T>(error: Error): Promise<T>;
        fromCanceled<T>(): Promise<T>;

        yield(): Promise<void>;
    }
}

// @ts-ignore
Promise.delay = function(delayOrMillisecondsDelay: TimeSpan | number, cancellationToken?: CancellationToken): Promise<void> {
    const delay = delayOrMillisecondsDelay instanceof TimeSpan
        ? delayOrMillisecondsDelay
        : TimeSpan.fromMilliseconds(delayOrMillisecondsDelay);

    return PromisePal.delay(delay, cancellationToken);
};

(Promise as any).completedPromise = PromisePal.completedPromise;
Promise.fromResult = PromisePal.fromResult;
Promise.fromError = PromisePal.fromError;
Promise.fromCanceled = PromisePal.fromCanceled;
Promise.yield = PromisePal.yield;
