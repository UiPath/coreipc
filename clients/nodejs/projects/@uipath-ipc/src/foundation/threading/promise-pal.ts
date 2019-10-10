// tslint:disable: only-arrow-functions

import { CancellationToken } from './cancellation-token';
import { CancellationTokenRegistration } from './cancellation-token-registration';
import { PromiseCompletionSource } from './promise-completion-source';
import { TimeSpan } from './timespan';
import { ArgumentError } from '../errors/argument-error';
import { OperationCanceledError } from '../errors/operation-canceled-error';
import { ArgumentNullError } from '@foundation/errors';

function subscribe<T>(promise: Promise<T>): Promise<T> {
    promise.then();
    return promise;
}

class PromisePal {
    public static readonly completedPromise = subscribe(new Promise<void>(resolve => resolve(undefined)));

    public static fromResult<T>(result: T): Promise<T> {
        return subscribe(new Promise<T>((resolve, _) => resolve(result)));
    }
    public static fromError<T>(error: Error): Promise<T> {
        return subscribe(new Promise<T>((_, reject) => reject(error)));
    }
    public static fromCanceled<T>(): Promise<T> {
        return subscribe(new Promise<T>((_, reject) => reject(new OperationCanceledError())));
    }

    public static delay(timespan: TimeSpan, cancellationToken: CancellationToken): Promise<void> {
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
        delay(delayOrMillisecondsDelay: number | TimeSpan, cancellationToken?: CancellationToken): Promise<void>;
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
Promise.delay = function (delayOrMillisecondsDelay: TimeSpan | number, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
    if (delayOrMillisecondsDelay == null) { throw new ArgumentNullError('delayOrMillisecondsDelay'); }

    let delay: TimeSpan;
    switch (typeof delayOrMillisecondsDelay) {
        case 'number':
            if (delayOrMillisecondsDelay < 0) {
                throw new ArgumentError('Cannot delay for a negative timespan.', 'millisecondsDelay');
            }
            delay = TimeSpan.fromMilliseconds(delayOrMillisecondsDelay);
            break;
        case 'object':
            if (!(delayOrMillisecondsDelay instanceof TimeSpan)) {
                throw new ArgumentError('Expecting a number or a TimeSpan as the first argument.');
            }
            if (delayOrMillisecondsDelay.isNegative) {
                throw new ArgumentError('Cannot delay for a negative timespan.', 'delay');
            }
            delay = delayOrMillisecondsDelay;
            break;
        default:
            throw new ArgumentError('Expecting a number or a TimeSpan as the first argument.');
    }

    return PromisePal.delay(delay, cancellationToken);
};

(Promise as any).completedPromise = PromisePal.completedPromise;
Promise.fromResult = PromisePal.fromResult;
Promise.fromError = PromisePal.fromError;
Promise.fromCanceled = PromisePal.fromCanceled;
Promise.yield = PromisePal.yield;
