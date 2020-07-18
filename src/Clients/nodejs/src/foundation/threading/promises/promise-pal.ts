import {
    argumentIs,
    TimeSpan,
    ArgumentOutOfRangeError,
    CancellationToken,
    PromiseCompletionSource,
    Trace,
} from '@foundation';

import { OperationCanceledError } from '@foundation-errors';

declare global {
    interface PromiseConstructor {
        readonly never: Promise<never>;

        delay(millisecondsDelay: number, cancellationToken?: CancellationToken): Promise<void>;
        delay(delay: TimeSpan, cancellationToken?: CancellationToken): Promise<void>;

        readonly completedPromise: Promise<void>;
        fromResult<T>(result: T): Promise<T>;
        fromError<T>(error: Error): Promise<T>;
        fromCanceled<T>(): Promise<T>;

        yield(): Promise<void>;
    }

    interface Promise<T> {
        observe(): Promise<T>;
        traceError(): void;
    }
}

Promise.prototype.observe = function <T>(this: Promise<T>): Promise<T> {
    this.then(emptyOnFulfilled, emptyOnRejected);
    return this;
};

const promiseNever: Promise<never> = {
    [Symbol.toStringTag]: 'promiseNever',

    then(_onfulfilled?: ((value: never) => any) | undefined | null, _onrejected?: ((reason: any) => any) | undefined | null): Promise<never> {
        return promiseNever;
    },
    catch(_onrejected?: ((reason: any) => any) | undefined | null): Promise<never> {
        return promiseNever;
    },
    finally(_onfinally?: (() => void) | undefined | null): Promise<never> {
        return promiseNever;
    },
    observe(): Promise<never> {
        return promiseNever;
    },
    traceError(): Promise<never> {
        return promiseNever;
    },
};

(Promise as any).never = promiseNever;
(Promise as any).completedPromise = new Promise<void>(resolve => resolve()).observe();
(Promise as any).fromResult = <T>(result: T): Promise<T> => {
    return new Promise<T>(resolve => resolve(result)).observe();
};
(Promise as any).fromError = (error: Error): Promise<unknown> => {
    return new Promise((_, reject) => reject(error)).observe();
};
(Promise as any).fromCanceled = (): Promise<unknown> => {
    return new Promise((_, reject) => reject(new OperationCanceledError())).observe();
};

Promise.delay = (arg0: TimeSpan | number, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> => {
    argumentIs(arg0, 'arg0', 'number', TimeSpan);
    const paramName = typeof arg0 === 'number' ? 'millisecondsDelay' : 'delay';
    arg0 = TimeSpan.toTimeSpan(arg0);

    if (arg0.isNegative && !arg0.isInfinite) {
        throw new ArgumentOutOfRangeError(paramName, 'Specified argument represented a negative interval.');
    }

    if (arg0.isInfinite) { return promiseNever; }

    const pcs = new PromiseCompletionSource<void>();

    const timeoutId = setTimeout(
        () => {
            pcs.setResult();
            if (maybeRegistration) { maybeRegistration.dispose(); }
        },
        arg0.totalMilliseconds);

    const maybeRegistration = cancellationToken.canBeCanceled
        ? cancellationToken.register(
            () => {
                clearTimeout(timeoutId);
                pcs.setCanceled();
            })
        : null;

    return pcs.promise;
};

Promise.yield = (): Promise<void> => {
    const pcs = new PromiseCompletionSource<void>();
    setTimeout(pcs.setResult.bind(pcs), 0);
    return pcs.promise;
};

function emptyOnFulfilled(result: any) { }
function emptyOnRejected(reason: any) { }