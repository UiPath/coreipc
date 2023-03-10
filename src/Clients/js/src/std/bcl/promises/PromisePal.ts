import {
    ArgumentOutOfRangeError,
    assertArgument,
    CancellationToken,
    OperationCanceledError,
    PromiseCompletionSource,
    TimeSpan,
} from '..';

function emptyOnRejected(reason: any) {}

const promiseNever: Promise<never> = {
    [Symbol.toStringTag]: 'promiseNever',

    then(
        _onfulfilled?: ((value: never) => any) | undefined | null,
        _onrejected?: ((reason: any) => any) | undefined | null,
    ): Promise<never> {
        return promiseNever;
    },
    catch(_onrejected?: ((reason: any) => any) | undefined | null): Promise<never> {
        return promiseNever;
    },
    finally(_onfinally?: (() => void) | undefined | null): Promise<never> {
        return promiseNever;
    },
};

export class PromisePal {
    public static get never(): Promise<never> {
        return promiseNever;
    }

    public static delay(
        arg0: TimeSpan | number,
        cancellationToken: CancellationToken = CancellationToken.none,
    ): Promise<void> {
        assertArgument({ arg0 }, 'number', TimeSpan);
        const paramName = typeof arg0 === 'number' ? 'millisecondsDelay' : 'delay';
        arg0 = TimeSpan.toTimeSpan(arg0);

        if (arg0.isNegative && !arg0.isInfinite) {
            throw new ArgumentOutOfRangeError(
                paramName,
                'Specified argument represented a negative interval.',
            );
        }

        if (arg0.isInfinite) {
            return promiseNever;
        }

        const pcs = new PromiseCompletionSource<void>();

        const timeoutId = setTimeout(() => {
            pcs.setResult();
            if (maybeRegistration) {
                maybeRegistration.dispose();
            }
        }, arg0.totalMilliseconds);

        const maybeRegistration = cancellationToken.canBeCanceled
            ? cancellationToken.register(() => {
                  clearTimeout(timeoutId);
                  pcs.setCanceled();
              })
            : null;

        return pcs.promise;
    }

    public static fromResult<T>(value: T): Promise<T> {
        return new Promise<T>(resolve => resolve(value));
    }

    public static fromError<T = unknown>(reason?: any): Promise<T> {
        return new Promise<T>((_, reject) => reject(reason));
    }

    public static fromCanceled<T = unknown>(): Promise<T> {
        return PromisePal.fromError<T>(new OperationCanceledError());
    }
}
