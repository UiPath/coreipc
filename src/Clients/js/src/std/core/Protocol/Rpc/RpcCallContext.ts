import {
    PromiseCompletionSource,
    CancellationToken,
    TimeSpan,
} from '../../../bcl';

import { RpcMessage, RpcCallContextBase } from '.';

/* @internal */
export type RpcCallContext = RpcCallContext.Incomming | RpcCallContext.Outgoing;

/* @internal */
export module RpcCallContext {
    export class Incomming extends RpcCallContextBase {
        constructor(
            public readonly request: RpcMessage.Request,
            public readonly respond: (
                response: RpcMessage.Response
            ) => Promise<void>
        ) {
            super();
        }
    }

    export class Outgoing extends RpcCallContextBase {
        private readonly _pcs =
            new PromiseCompletionSource<RpcMessage.Response>();

        constructor(timeout: TimeSpan, ct: CancellationToken) {
            super();
            timeout.bind(this._pcs);
            ct.bind(this._pcs);
        }

        public get promise(): Promise<RpcMessage.Response> {
            return this._pcs.promise;
        }

        public complete(response: RpcMessage.Response): void {
            const _ = this._pcs.trySetResult(response);
        }
    }
}
