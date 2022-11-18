import { Address, IMessageStream, IRpcChannelFactory } from '..';
import { ChannelManager, IServiceProvider, ProxyId } from '.';

/* @internal */
export class ChannelManagerStore {
    invokeMethod<TService, TAddress extends Address>(
        proxyId: ProxyId<TService, TAddress>,
        methodName: keyof TService & string,
        args: unknown[],
    ): Promise<unknown> {
        const channel = this.getOrCreateChannel<TAddress>(proxyId.address);

        const result = channel.invokeMethod(proxyId.serviceId, methodName, args);

        return result;
    }

    constructor(
        private readonly _domain: IServiceProvider,
        private readonly _rpcChannelFactory: IRpcChannelFactory,
        private readonly _messageStreamFactory: IMessageStream.Factory,
    ) {}

    private getOrCreateChannel<TAddress extends Address>(address: TAddress): ChannelManager {
        let channel = this._map.get(address.key);

        if (channel === undefined) {
            channel = new ChannelManager<TAddress>(
                this._domain,
                address,
                this._rpcChannelFactory,
                this._messageStreamFactory,
            );
            this._map.set(address.key, channel);
        }

        return channel;
    }

    private readonly _map = new Map<string, ChannelManager<any>>();
}
