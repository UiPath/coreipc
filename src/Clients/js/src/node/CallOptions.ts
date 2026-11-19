import { AsyncLocalStorage } from 'async_hooks';
import { AmbientCallOptions, CallOptions, TimeSpan } from '../std';

const storage = new AsyncLocalStorage<CallOptions>();

AmbientCallOptions.install(() => storage.getStore());

/**
 * Applies `options` to every IPC call made within `callback`, including asynchronous
 * continuations of it. Nests: an inner scope wins for its own duration.
 *
 * An explicit `Message` argument on a call still overrides this.
 *
 * Node only — it is not exported from the web entry point, which has no way to track the
 * current asynchronous flow.
 */
export function callOptions<T>(options: CallOptions, callback: () => T): T {
    return storage.run(options, callback);
}

/** Convenience for the common case of bounding calls by a deadline. */
export function withRequestTimeout<T>(requestTimeout: TimeSpan, callback: () => T): T {
    return callOptions({ requestTimeout }, callback);
}
