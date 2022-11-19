import {
    ConfigStore,
    IContractStore,
    DispatchProxyStore,
    CallbackStoreImpl,
    ChannelManagerStore,
    ProxyStore,
    AddressBuilder,
} from '.';

/* @internal */
export interface IServiceProvider<TAddressBuilder extends AddressBuilder = any> {
    readonly configStore: ConfigStore;
    readonly proxyStore: ProxyStore;
    readonly dispatchProxyStore: DispatchProxyStore;
    readonly channelStore: ChannelManagerStore;
    readonly contractStore: IContractStore;
    readonly callbackStore: CallbackStoreImpl;

    createAddressBuilder(): TAddressBuilder;
}
