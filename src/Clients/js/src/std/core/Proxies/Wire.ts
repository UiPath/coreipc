import { Address, IMessageStream, IRpcChannelFactory, MessageStream, RpcChannel } from '..';
import { ChannelManager, IServiceProvider, ProxyId } from '.';
import { Dictionary } from '../../bcl';

/* @internal */
export class Wire {
    invokeMethod<TService>(proxyId: ProxyId<TService>, methodName: keyof TService & string, args: unknown[]): Promise<unknown> {
        const channelManager = this.getOrCreateChannelManager(proxyId.address);

        return channelManager.invokeMethod(proxyId.service, methodName, args);
    }

    constructor(
        private readonly _sp: IServiceProvider,
        private readonly _rpcChannelFactory: IRpcChannelFactory = RpcChannel,
        private readonly _messageStreamFactory: IMessageStream.Factory = new MessageStream.Factory(),
    ) { }

    private getOrCreateChannelManager(address: Address): ChannelManager {
        return this._map.getOrCreateValue(
            address.key,
            () => new ChannelManager(
                this._sp,
                address,
                this._rpcChannelFactory,
                this._messageStreamFactory));
    }

    async disposeChannel(addressKey: string): Promise<void> {
        const channelManager = this._map.get(addressKey);
        if (channelManager) {
            this._map.delete(addressKey);
            await channelManager.disposeAsync();
        }
    }

    async disposeAllChannels(): Promise<void> {
        const channelManagers = [...this._map.values()];
        this._map.clear();

        await Promise.all(channelManagers.map(cm => cm.disposeAsync()));
    }

    private readonly _map = new Dictionary<string, ChannelManager>();
}
