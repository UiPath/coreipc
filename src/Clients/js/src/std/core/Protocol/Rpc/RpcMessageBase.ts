import { Network } from '..';

/* @internal */
export abstract class RpcMessageBase {
    public abstract toNetwork(): Network.Message;
}
