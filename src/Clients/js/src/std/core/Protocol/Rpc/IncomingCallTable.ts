import { CancellationToken, CancellationTokenSource } from '../../../bcl';

/**
 * The callee mirror of the outgoing-call table: tracks in-flight incoming calls by
 * request id so an inbound `CancellationRequest` can cancel the running handler.
 */
/* @internal */
export class IncomingCallTable {
    private readonly _map = new Map<string, CancellationTokenSource>();

    /** Tracks `requestId`; the returned token fires when a cancellation for it arrives. */
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
