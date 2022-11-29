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
export interface IServiceProvider<
    TAddressBuilder extends AddressBuilder = any,
> {
    readonly symbolofServiceAttachment: symbol;

    readonly configStore: ConfigStore<TAddressBuilder>;
    readonly proxyStore: ProxyStore<TAddressBuilder>;
    readonly dispatchProxyStore: DispatchProxyStore;
    readonly channelStore: ChannelManagerStore;
    readonly contractStore: IContractStore;
    readonly callbackStore: CallbackStoreImpl;

    createAddressBuilder(): TAddressBuilder;
}
