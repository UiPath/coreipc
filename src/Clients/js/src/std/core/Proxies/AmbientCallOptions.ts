import { TimeSpan } from '../..';

/**
 * Options applied to every outgoing call on the current asynchronous flow.
 */
export interface CallOptions {
    readonly requestTimeout?: TimeSpan;
}

export type CallOptionsProvider = () => CallOptions | undefined;

/**
 * Read side of the ambient per-call options.
 *
 * NODE ONLY. Tracking "the current asynchronous flow" needs `AsyncLocalStorage`, which browsers
 * have no equivalent of, so only the Node entry point installs a provider (see `callOptions`).
 * In the web build nothing installs one, `current()` is always `undefined`, and calls fall back
 * to the client-wide timeout exactly as they did before this existed.
 *
 * Lives here rather than in `../../../node` because `RpcRequestFactory` is shared code and cannot
 * import from a platform entry point.
 */
export module AmbientCallOptions {
    let provider: CallOptionsProvider | undefined;

    /** Installed by the Node entry point at import time. */
    export function install(value: CallOptionsProvider | undefined): void {
        provider = value;
    }

    /** The options in force on this flow, or `undefined` (always, outside Node). */
    export function current(): CallOptions | undefined {
        return provider?.();
    }
}
