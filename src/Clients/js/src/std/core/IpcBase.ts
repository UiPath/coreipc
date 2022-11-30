import { PublicCtor, ParameterlessPublicCtor, assertArgument, TimeSpan } from '../bcl';

import {
    Address,
    AddressBuilder,
    AddressSelectionDelegate,
    ConfigStore,
    RpcChannel,
    MessageStream,
    ConnectHelper,
    ServiceAnnotations,
    OperationAnnotations,
    ServiceAnnotationsWrapper,
    OperationAnnotationsWrapper,
} from '.';

import {
    ProxyId,
    ProxyStore,
    IServiceProvider,
    DispatchProxyStore,
    ChannelManagerStore,
    CallbackStoreImpl,
} from './Proxies';
import { ContractStore, IContractStore } from './Contract';
import { Callback, CallbackImpl } from './Callbacks';

export interface IpcBase<TAddressBuilder extends AddressBuilder> {
    readonly $service: ServiceAnnotations;
    readonly $operation: OperationAnnotations;

    readonly proxy: IpcBase.ProxySource<TAddressBuilder>;
    readonly config: IpcBase.Configuration<TAddressBuilder>;
    readonly callback: Callback<TAddressBuilder>;
}

export module IpcBase {
    export interface ProxySource<TAddressBuilder extends AddressBuilder> {
        withAddress<TAddress extends Address>(
            configure: AddressSelectionDelegate<TAddressBuilder, TAddress>,
        ): ProxySourceWithAddress;
    }

    export interface ProxySourceWithAddress {
        withService<TService>(service: PublicCtor<TService>): TService;
    }

    export interface Configuration<TAddressBuilder extends AddressBuilder> {
        forAnyAddress(): ConfigurationWithAddress;

        forAddress<TAddress extends Address>(
            configure: AddressSelectionDelegate<TAddressBuilder, TAddress>,
        ): ConfigurationWithAddress<TAddress>;
    }

    export interface ConfigurationWithAddress<TAddress extends Address = Address> {
        setConnectHelper(value: ConnectHelper<TAddress>): void;

        forAnyService(): ConfigurationWithAddressService<TAddress>;

        forService<TService>(
            service: PublicCtor<TService>,
        ): ConfigurationWithAddressService<TAddress, TService>;
    }

    export interface ConfigurationWithAddressService<
        TAddress extends Address = Address,
        TService = any,
    > {
        setRequestTimeout(value: number | TimeSpan): void;
    }
}

/* @internal */
export abstract class IpcBaseImpl<TAddressBuilder extends AddressBuilder = any>
    implements IpcBase<TAddressBuilder>, IServiceProvider<TAddressBuilder>
{
    constructor(
        /* @internal */
        public readonly addressBuilder: ParameterlessPublicCtor<TAddressBuilder>,
        /* @internal */
        $service?: ServiceAnnotations,
        /* @internal */
        $operation?: OperationAnnotations,
    ) {
        this.$service = $service ?? new ServiceAnnotationsWrapper(this).iface;
        this.$operation = $operation ?? new OperationAnnotationsWrapper(this).iface;
    }

    readonly $service: ServiceAnnotations;
    readonly $operation: OperationAnnotations;

    createAddressBuilder(): TAddressBuilder {
        return new this.addressBuilder();
    }

    readonly symbolofServiceAttachment: symbol = Symbol('ServiceAttachment');

    public readonly proxy: IpcBaseImpl.ProxySource<TAddressBuilder> =
        new IpcBaseImpl.ProxySource<TAddressBuilder>(this);

    public readonly config: IpcBaseImpl.Configuration<TAddressBuilder> =
        new IpcBaseImpl.Configuration<TAddressBuilder>(this);

    public readonly callback: Callback<TAddressBuilder> = new CallbackImpl<TAddressBuilder>(this);

    /* @internal */
    public readonly configStore: ConfigStore<TAddressBuilder> = new ConfigStore<TAddressBuilder>(
        this,
    );

    /* @internal */
    public readonly proxyStore: ProxyStore<TAddressBuilder> = new ProxyStore<TAddressBuilder>(this);

    /* @internal */
    public readonly dispatchProxyStore: DispatchProxyStore = new DispatchProxyStore(this);

    /* @internal */
    public readonly channelStore: ChannelManagerStore = new ChannelManagerStore(
        this,
        RpcChannel,
        new MessageStream.Factory(),
    );

    /* @internal */
    public readonly contractStore: IContractStore = new ContractStore();

    /* @internal */
    public readonly callbackStore = new CallbackStoreImpl();
}

/* @internal */
export module IpcBaseImpl {
    export class ProxySource<TAddressBuilder extends AddressBuilder>
        implements IpcBase.ProxySource<TAddressBuilder>
    {
        constructor(private readonly _ipc: IpcBaseImpl<TAddressBuilder>) {}

        public withAddress<TAddress extends Address>(
            configure: AddressSelectionDelegate<TAddressBuilder, TAddress>,
        ): ProxySourceWithAddress<TAddress> {
            const builder = new this._ipc.addressBuilder();
            const type = configure(builder);
            const address = builder.assertAddress<TAddress>(type);

            return new ProxySourceWithAddress(this._ipc, address);
        }
    }

    export class ProxySourceWithAddress<TAddress extends Address>
        implements IpcBase.ProxySourceWithAddress
    {
        constructor(private readonly _ipc: IpcBaseImpl, private readonly _address: TAddress) {}

        public withService<TService>(service: PublicCtor<TService>): TService {
            const proxyId = new ProxyId<TService, TAddress>(service, this._address);

            const proxy = this._ipc.proxyStore.resolve(proxyId);
            return proxy;
        }
    }

    export class Configuration<TAddressBuilder extends AddressBuilder> {
        constructor(private readonly _ipc: IpcBaseImpl<TAddressBuilder>) {}

        public forAnyAddress(): ConfigurationWithAddress {
            return new ConfigurationWithAddress(this._ipc);
        }

        public forAddress<TAddress extends Address>(
            configure: AddressSelectionDelegate<TAddressBuilder, TAddress>,
        ): ConfigurationWithAddress<TAddress> {
            const builder = new this._ipc.addressBuilder();
            const type = configure(builder);
            const address = builder.assertAddress<TAddress>(type);

            return new ConfigurationWithAddress(this._ipc, address);
        }
    }

    export class ConfigurationWithAddress<TAddress extends Address = Address> {
        constructor(private readonly _ipc: IpcBaseImpl, private readonly _address?: TAddress) {}

        public setConnectHelper(value: ConnectHelper<TAddress>): void {
            assertArgument({ value }, 'function');
            this._ipc.configStore.setConnectHelper(this._address, value);
        }

        public forAnyService(): ConfigurationWithAddressService<TAddress> {
            return new ConfigurationWithAddressService(this._ipc, this._address);
        }

        public forService<TService>(
            service: PublicCtor<TService>,
        ): ConfigurationWithAddressService<TAddress, TService> {
            return new ConfigurationWithAddressService(this._ipc, this._address, service);
        }
    }

    export class ConfigurationWithAddressService<
        TAddress extends Address = Address,
        TService = any,
    > {
        constructor(
            private readonly _ipc: IpcBaseImpl,
            private readonly _address: TAddress | undefined,
            private readonly _service?: PublicCtor<TService>,
        ) {}

        public setRequestTimeout(value: number | TimeSpan): void {
            assertArgument({ value }, 'number', TimeSpan);

            if (typeof value === 'number') {
                value = TimeSpan.fromMilliseconds(value);
            }

            this._ipc.configStore.setRequestTimeout(this._address, this._service, value);
        }
    }
}
