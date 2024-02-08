import { PublicCtor, ParameterlessPublicCtor, assertArgument, TimeSpan } from '../bcl';

import {
    Address,
    AddressBuilder,
    AddressSelectionDelegate,
    ConfigStore,
    ConnectHelper,
    ServiceAnnotations,
    OperationAnnotations,
    ServiceAnnotationsWrapper,
    OperationAnnotationsWrapper,
} from '.';

import {
    ProxyId,
    ProxySource,
    IServiceProvider,
    DispatchProxyClassStore,
    Wire,
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
        withAddress(configure: AddressSelectionDelegate<TAddressBuilder>): ProxySourceWithAddress;
    }

    export interface ProxySourceWithAddress {
        withService<TService>(service: PublicCtor<TService>): TService;
    }

    export interface Configuration<TAddressBuilder extends AddressBuilder> {
        forAnyAddress(): ConfigurationWithAddress;

        forAddress<TAddress extends Address>(configure: AddressSelectionDelegate<TAddressBuilder>): ConfigurationWithAddress<TAddress>;
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

export abstract class IpcCoreBase {
    // DB
    /* @internal */
    public readonly _db: Map<string, any> = new Map<string, any>();
}

/* @internal */
export abstract class IpcBaseImpl<TAddressBuilder extends AddressBuilder = any>
    implements IpcBase<TAddressBuilder>, IServiceProvider<TAddressBuilder>
{
    constructor(
        /* @internal */
        private readonly addressBuilder: ParameterlessPublicCtor<TAddressBuilder>,
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

    public readonly proxy: IpcBaseImpl.ProxySource<TAddressBuilder> = new IpcBaseImpl.ProxySource<TAddressBuilder>(this);

    public readonly config: IpcBase.Configuration<TAddressBuilder> = new IpcBaseImpl.Configuration<TAddressBuilder>(this);

    public readonly callback: Callback<TAddressBuilder> = new CallbackImpl<TAddressBuilder>(this);

    /* @internal */
    public readonly configStore: ConfigStore<TAddressBuilder> = new ConfigStore<TAddressBuilder>(this);

    /* @internal */
    public readonly proxySource: ProxySource<TAddressBuilder> = new ProxySource<TAddressBuilder>(this);

    /* @internal */
    public readonly dispatchProxies: DispatchProxyClassStore = new DispatchProxyClassStore();

    /* @internal */
    public readonly wire: Wire = new Wire(this);

    /* @internal */
    public readonly contractStore: IContractStore = new ContractStore();

    /* @internal */
    public readonly callbackStore = new CallbackStoreImpl();

    /* @internal */
    public getAddress(configure: AddressSelectionDelegate<TAddressBuilder>): Address {
        const builder = new this.addressBuilder();
        configure(builder);
        return builder.assertAddress();
    }
}

/* @internal */
export module IpcBaseImpl {
    export class ProxySource<TAddressBuilder extends AddressBuilder>
        implements IpcBase.ProxySource<TAddressBuilder>
    {
        constructor(private readonly _serviceProvider: IServiceProvider<TAddressBuilder>) {}

        public withAddress(configure: AddressSelectionDelegate<TAddressBuilder>): ProxySourceWithAddress {
            const address = this._serviceProvider.getAddress(configure);

            return new ProxySourceWithAddress(this._serviceProvider, address);
        }
    }

    export class ProxySourceWithAddress
        implements IpcBase.ProxySourceWithAddress
    {
        constructor(
            private readonly _serviceProvider: IServiceProvider,
            private readonly _address: Address) {}

        public withService<TService>(service: PublicCtor<TService>): TService {
            const proxyId = new ProxyId<TService>(service, this._address);

            const proxy = this._serviceProvider.proxySource.resolve(proxyId);
            return proxy;
        }
    }

    export class Configuration<TAddressBuilder extends AddressBuilder> {
        constructor(private readonly _ipc: IServiceProvider<TAddressBuilder>) {}

        public forAnyAddress(): ConfigurationWithAddress {
            return new ConfigurationWithAddress(this._ipc);
        }

        public forAddress(configure: AddressSelectionDelegate<TAddressBuilder>): ConfigurationWithAddress {
            const address = this._ipc.getAddress(configure);

            return new ConfigurationWithAddress(this._ipc, address);
        }
    }

    export class ConfigurationWithAddress implements IpcBase.ConfigurationWithAddress<any> {
        constructor(private readonly _ipc: IServiceProvider, private readonly _address?: Address) {}

        public setConnectHelper(value: ConnectHelper): void {
            assertArgument({ value }, 'function');
            this._ipc.configStore.setConnectHelper(this._address, value);
        }

        public forAnyService(): ConfigurationWithAddressService {
            return new ConfigurationWithAddressService(this._ipc, this._address);
        }

        public forService<TService>(service: PublicCtor<TService>): ConfigurationWithAddressService<TService> {
            return new ConfigurationWithAddressService(this._ipc, this._address, service);
        }
    }

    export class ConfigurationWithAddressService<TService = unknown> {
        constructor(
            private readonly _ipc: IServiceProvider,
            private readonly _address: Address | undefined,
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
