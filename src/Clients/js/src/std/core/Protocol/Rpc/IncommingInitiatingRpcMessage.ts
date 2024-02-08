import { RpcMessage } from '.';

/* @internal */
export type IncommingInitiatingRpcMessage = RpcMessage.Request | RpcMessage.CancellationRequest;
