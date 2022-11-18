import { CallbackStore, ChannelManagerStore, ProxyStore } from '.';
import { ConfigStore, IContractStore } from '..';
import { DispatchProxyStore } from './DispatchProxy';

/* @internal */
export interface IProxiesDomain {
    readonly configStore: ConfigStore;
    readonly proxyStore: ProxyStore;
    readonly dispatchProxyStore: DispatchProxyStore;
    readonly channelStore: ChannelManagerStore;
    readonly contractStore: IContractStore;
    readonly callbackStore: CallbackStore;
}
