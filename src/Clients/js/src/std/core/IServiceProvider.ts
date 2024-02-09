import {
    ConfigStore,
    IContractStore,
    DispatchProxyClassStore,
    CallbackStoreImpl,
    Wire,
    ProxySource,
    AddressBuilder,
    AddressSelectionDelegate,
    Address,
} from '.';

/* @internal */
export interface IServiceProvider<TAddressBuilder extends AddressBuilder = AddressBuilder> {
    readonly configStore: ConfigStore<TAddressBuilder>;
    readonly proxySource: ProxySource<TAddressBuilder>;
    readonly dispatchProxies: DispatchProxyClassStore;
    readonly wire: Wire;
    readonly contractStore: IContractStore;
    readonly callbackStore: CallbackStoreImpl;

    getAddress(configure: AddressSelectionDelegate<TAddressBuilder>): Address;
}
