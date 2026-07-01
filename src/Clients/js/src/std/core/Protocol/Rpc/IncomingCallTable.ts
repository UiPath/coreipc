import { CancellationToken, CancellationTokenSource } from '../../../bcl';

/**
 * Tracks in-flight *incoming* calls (callbacks the peer invokes on us) by their
 * request id, each backed by a {@link CancellationTokenSource}. It is the callee
 * mirror of the outgoing-call table: it lets an inbound `CancellationRequest`
 * frame cancel the matching running handler.
 *
 * @internal
 */
/* @internal */
export class IncomingCallTable {
    private readonly _map = new Map<string, CancellationTokenSource>();

    /**
     * Starts tracking `requestId` and returns the token that fires when a
     * cancellation for it arrives (or the call is torn down).
     */
    public register(requestId: string): CancellationToken {
        const cts = new CancellationTokenSource();
        this._map.set(requestId, cts);
        return cts.token;
    }

    /** Cancels the tracked call `requestId`, if still in flight. */
    public tryCancel(requestId: string): void {
        const cts = this._map.get(requestId);
        if (cts && !cts.isCancellationRequested) {
            cts.cancel();
        }
    }

    /** Stops tracking `requestId` (the call finished) and releases its source. */
    public complete(requestId: string): void {
        const cts = this._map.get(requestId);
        if (cts) {
            this._map.delete(requestId);
            cts.dispose();
        }
    }

    /** Cancels and releases every tracked call — used when the channel dies. */
    public clear(): void {
        for (const cts of this._map.values()) {
            if (!cts.isCancellationRequested) {
                cts.cancel();
            }
            cts.dispose();
        }
        this._map.clear();
    }
}
