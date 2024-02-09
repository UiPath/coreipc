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

    private readonly _map = new Dictionary<string, ChannelManager>();
}
