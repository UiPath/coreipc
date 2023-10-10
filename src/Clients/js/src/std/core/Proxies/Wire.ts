import { Address, IMessageStream, IRpcChannelFactory, MessageStream, RpcChannel } from '..';
import { ChannelManager, IServiceProvider, ProxyId } from '.';
import { Dictionary } from '../../bcl';

/* @internal */
export class Wire {
    invokeMethod<TService, TAddress extends Address>(proxyId: ProxyId<TService, TAddress>, methodName: keyof TService & string, args: unknown[]): Promise<unknown> {
        const channelManager = this.getOrCreateChannelManager<TAddress>(proxyId.address);

        const result = channelManager.invokeMethod(proxyId.service, methodName, args);

        return result;
    }

    constructor(
        private readonly _sp: IServiceProvider,
        private readonly _rpcChannelFactory: IRpcChannelFactory = RpcChannel,
        private readonly _messageStreamFactory: IMessageStream.Factory = new MessageStream.Factory(),
    ) { }

    private getOrCreateChannelManager<TAddress extends Address>(address: TAddress): ChannelManager {
        return this._map.getOrCreateValue(
            address.key,
            () => new ChannelManager<TAddress>(
                this._sp,
                address,
                this._rpcChannelFactory,
                this._messageStreamFactory));
    }

    private readonly _map = new Dictionary<string, ChannelManager<any>>();
}
