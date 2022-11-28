import {
    PublicCtor,
    ParameterlessPublicCtor,
    assertArgument,
    TimeSpan,
} from '../bcl';

import {
    Address,
    AddressBuilder,
    ServiceId,
    AddressSelectionDelegate,
    ConfigStore,
    RpcChannel,
    MessageStream,
    ConnectHelper,
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

export abstract class Ipc<TAddressBuilder extends AddressBuilder = any>
    implements IServiceProvider<TAddressBuilder>
{
    constructor(
        /* @internal */
        public readonly addressBuilder: ParameterlessPublicCtor<TAddressBuilder>,
    ) {}

    createAddressBuilder(): TAddressBuilder {
        return new this.addressBuilder();
    }

    public readonly proxy: Ipc.ProxySource<TAddressBuilder> =
        new Ipc.ProxySource<TAddressBuilder>(this);

    public readonly config: Ipc.Configuration<TAddressBuilder> =
        new Ipc.Configuration<TAddressBuilder>(this);

    public readonly callback: Callback<TAddressBuilder> =
        new CallbackImpl<TAddressBuilder>(this);

    /* @internal */
    public readonly configStore = new ConfigStore();

    /* @internal */
    public readonly proxyStore: ProxyStore = new ProxyStore(this);

    /* @internal */
    public readonly dispatchProxyStore: DispatchProxyStore =
        new DispatchProxyStore(this);

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

export module Ipc {
    export class ProxySource<TAddressBuilder extends AddressBuilder> {
        /* @internal */
        constructor(private readonly _ipc: Ipc<TAddressBuilder>) {}

        public withAddress<TAddress extends Address>(
            configure: AddressSelectionDelegate<TAddressBuilder, TAddress>,
        ): ProxySourceWithAddress<TAddress> {
            const builder = new this._ipc.addressBuilder();
            const type = configure(builder);
            const address = builder.assertAddress<TAddress>(type);

            return new ProxySourceWithAddress(this._ipc, address);
        }
    }

    export class ProxySourceWithAddress<TAddress extends Address> {
        /* @internal */
        constructor(
            private readonly _ipc: Ipc,
            private readonly _address: TAddress,
        ) {}

        public withService<TService>(
            service: PublicCtor<TService>,
            endpointName?: string,
        ): TService {
            const serviceId = new ServiceId<TService>(service, endpointName);
            const proxyId = new ProxyId<TService, TAddress>(
                serviceId,
                this._address,
            );

            const proxy = this._ipc.proxyStore.resolve(proxyId);
            return proxy;
        }
    }

    export class Configuration<TAddressBuilder extends AddressBuilder> {
        /* @internal */
        constructor(private readonly _ipc: Ipc<TAddressBuilder>) {}

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
        /* @internal */
        constructor(
            private readonly _ipc: Ipc,
            private readonly _address?: TAddress,
        ) {}

        public setConnectHelper(value: ConnectHelper<TAddress>): void {
            assertArgument({ value }, 'function');
            this._ipc.configStore.setConnectHelper(this._address, value);
        }

        public forAnyService(): ConfigurationWithAddressService<TAddress> {
            return new ConfigurationWithAddressService(
                this._ipc,
                this._address,
            );
        }

        public forService<TService>(
            service: PublicCtor<TService>,
            endpointName?: string,
        ): ConfigurationWithAddressService<TAddress, TService> {
            return new ConfigurationWithAddressService(
                this._ipc,
                this._address,
                new ServiceId<TService>(service, endpointName),
            );
        }
    }

    export class ConfigurationWithAddressService<
        TAddress extends Address = Address,
        TService = any,
    > {
        /* @internal */
        constructor(
            private readonly _ipc: Ipc,
            private readonly _address: TAddress | undefined,
            private readonly _serviceId?: ServiceId<TService>,
        ) {}

        public setRequestTimeout(value: number | TimeSpan): void {
            assertArgument({ value }, 'number', TimeSpan);

            if (typeof value === 'number') {
                value = TimeSpan.fromMilliseconds(value);
            }

            this._ipc.configStore.setRequestTimeout(
                this._address,
                this._serviceId,
                value,
            );
        }
    }
}
