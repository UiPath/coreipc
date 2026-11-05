import { CancellationToken } from './CancellationToken';
import { CancellationTokenSource } from './CancellationTokenSource';

/**
 * Bridges a Web/Node `AbortSignal` to a `CancellationToken`, so a contract method may
 * accept an `AbortSignal` anywhere a `CancellationToken` is accepted.
 */
export class AbortSignalAdapter {
    /** Whether `arg` is an `AbortSignal` (guarded for runtimes without it). */
    public static isAbortSignal(arg: unknown): arg is AbortSignal {
        return typeof AbortSignal !== 'undefined' && arg instanceof AbortSignal;
    }

    /**
     * `candidate` unchanged if it is a `CancellationToken`, the bridged token if it is
     * an `AbortSignal`, or `undefined` if it is neither cancellation shape.
     */
    public static ensureCancellationToken(
        candidate: unknown,
    ): CancellationToken | undefined {
        if (candidate instanceof CancellationToken) {
            return candidate;
        }
        if (AbortSignalAdapter.isAbortSignal(candidate)) {
            return AbortSignalAdapter.toCancellationToken(candidate);
        }
        return undefined;
    }

    /**
     * A `CancellationToken` cancelled when `signal` aborts — immediately if already
     * aborted. The `once` listener self-removes, so a never-aborting signal leaks nothing.
     */
    public static toCancellationToken(signal: AbortSignal): CancellationToken {
        const cts = new CancellationTokenSource();
        if (signal.aborted) {
            cts.cancel();
            cts.dispose();
        } else {
            signal.addEventListener(
                'abort',
                () => {
                    try {
                        cts.cancel();
                    } finally {
                        cts.dispose();
                    }
                },
                { once: true },
            );
        }
        return cts.token;
    }

    /**
     * The reverse of {@link toCancellationToken}, for handing a callback handler an
     * `AbortSignal`. Throws if the runtime has no `AbortController`.
     */
    public static toAbortSignal(token: CancellationToken): AbortSignal {
        if (typeof AbortController === 'undefined') {
            throw new Error(
                'Cannot adapt a CancellationToken to an AbortSignal: this runtime has no AbortController.',
            );
        }
        const controller = new AbortController();
        if (token.isCancellationRequested) {
            controller.abort();
        } else {
            token.register(() => controller.abort());
        }
        return controller.signal;
    }
}
