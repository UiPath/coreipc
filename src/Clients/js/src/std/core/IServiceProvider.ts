import {
    ConfigStore,
    IContractStore,
    DispatchProxyClassStore,
    CallbackStoreImpl,
    Wire,
    ProxySource,
    AddressBuilder,
} from '.';

/* @internal */
export interface IServiceProvider<
    TAddressBuilder extends AddressBuilder = any,
> {
    readonly configStore: ConfigStore<TAddressBuilder>;
    readonly proxySource: ProxySource<TAddressBuilder>;
    readonly dispatchProxies: DispatchProxyClassStore;
    readonly wire: Wire;
    readonly contractStore: IContractStore;
    readonly callbackStore: CallbackStoreImpl;

    createAddressBuilder(): TAddressBuilder;
}
