import { PromiseCompletionSource, CancellationToken, TimeSpan } from '../../../bcl';

import { RpcMessage } from '.';

/* @internal */
export type RpcCallContext = RpcCallContext.Incomming | RpcCallContext.Outgoing;

/* @internal */
export module RpcCallContext {
    export class Incomming {
        constructor(
            public readonly request: RpcMessage.Request,
            public readonly respond: (response: RpcMessage.Response) => Promise<void>,
            /** Cancelled when the peer sends a `CancellationRequest` for this call. */
            public readonly ct: CancellationToken = CancellationToken.none,
        ) {}
    }

    export class Outgoing {
        private readonly _pcs = new PromiseCompletionSource<RpcMessage.Response>();

        constructor(timeout: TimeSpan, ct: CancellationToken) {
            timeout.bind(this._pcs);
            ct.bind(this._pcs);
        }

        public get promise(): Promise<RpcMessage.Response> {
            return this._pcs.promise;
        }

        public complete(response: RpcMessage.Response): void {
            const _ = this._pcs.trySetResult(response);
        }

        /** Faults the pending call — used when the channel is torn down. */
        public fail(error: Error): void {
            const _ = this._pcs.trySetFaulted(error);
        }
    }
}
