import { CancellationToken } from './CancellationToken';
import { CancellationTokenSource } from './CancellationTokenSource';

/**
 * Bridges a Web/Node `AbortSignal` to a UiPath.Ipc `CancellationToken`, so a
 * contract method may accept an `AbortSignal` anywhere a `CancellationToken` is
 * accepted. Purely additive: `CancellationToken` / `CancellationTokenSource`
 * are unchanged and remain fully supported.
 */
export class AbortSignalAdapter {
    /** Whether `arg` is an `AbortSignal` (guarded for runtimes without it). */
    public static isAbortSignal(arg: unknown): arg is AbortSignal {
        return typeof AbortSignal !== 'undefined' && arg instanceof AbortSignal;
    }

    /**
     * Check-and-adapt in one gulp: returns `candidate` unchanged if it is
     * already a `CancellationToken`, the bridged token if it is an
     * `AbortSignal`, or `undefined` if it is neither (i.e. not a cancellation
     * argument). Lets callers treat both cancellation shapes uniformly.
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
     * Returns a `CancellationToken` that is cancelled when `signal` aborts —
     * immediately if it is already aborted.
     *
     * The backing `CancellationTokenSource` is created with no `cancelAfter`
     * delay, so its only disposable resource (the delay timer) is never
     * allocated; we still `dispose()` it right after it fires — for hygiene and
     * to stay correct if the source ever gains resources. A never-aborting
     * signal's source holds nothing and is collected together with the signal
     * (the `abort` listener is registered `once`, so it self-removes).
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
}
