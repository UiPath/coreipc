import {
    ConfigStore,
    IContractStore,
    DispatchProxyStore,
    CallbackStore,
    ChannelManagerStore,
    ProxyStore,
} from '.';

/* @internal */
export interface IServiceProvider {
    readonly configStore: ConfigStore;
    readonly proxyStore: ProxyStore;
    readonly dispatchProxyStore: DispatchProxyStore;
    readonly channelStore: ChannelManagerStore;
    readonly contractStore: IContractStore;
    readonly callbackStore: CallbackStore;
}
