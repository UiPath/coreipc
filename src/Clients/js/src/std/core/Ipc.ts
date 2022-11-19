import { PublicCtor, ParameterlessPublicCtor } from '../bcl';

import {
    Address,
    AddressBuilder,
    ServiceId,
    UpdateConfigDelegate,
    AddressSelectionDelegate,
    ConfigStore,
    RpcChannel,
    MessageStream,
} from '.';

import {
    ProxyId,
    ProxyStore,
    IServiceProvider,
    DispatchProxyStore,
    ChannelManagerStore,
    CallbackStore,
} from './Proxies';
import { ContractStore, IContractStore } from './Contract';

export abstract class Ipc<TAddressBuilder extends AddressBuilder = any> implements IServiceProvider {
    constructor(
        /* @internal */
        public readonly addressBuilder: ParameterlessPublicCtor<TAddressBuilder>,
    ) {}

    public readonly proxy: Ipc.ProxySource<TAddressBuilder> = new Ipc.ProxySource<TAddressBuilder>(
        this,
    );

    public readonly config: Ipc.Configuration<TAddressBuilder> =
        new Ipc.Configuration<TAddressBuilder>(this);

    /* @internal */
    public readonly configStore = new ConfigStore();

    /* @internal */
    public readonly proxyStore: ProxyStore = new ProxyStore(this);

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
    public readonly callbackStore: CallbackStore = null!;
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
        constructor(private readonly _ipc: Ipc, private readonly _address: TAddress) {}

        public withService<TService>(
            service: PublicCtor<TService>,
            endpointName?: string,
        ): TService {
            const serviceId = new ServiceId<TService>(service, endpointName);
            const proxyId = new ProxyId<TService, TAddress>(serviceId, this._address);

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
        constructor(private readonly _ipc: Ipc, private readonly _address?: TAddress) {}

        public forAnyService(): ConfigurationWithAddressService<TAddress> {
            return new ConfigurationWithAddressService(this._ipc, this._address);
        }
    }

    export class ConfigurationWithAddressService<TAddress extends Address = Address> {
        /* @internal */
        constructor(
            private readonly _ipc: Ipc,
            private readonly _address: TAddress | undefined,
            private readonly _serviceId?: ServiceId,
        ) {}

        public update(updateAction: UpdateConfigDelegate<TAddress>) {
            const builder = this._ipc.configStore.getBuilder<TAddress>(
                this._address,
                this._serviceId,
            );

            updateAction(builder);
        }
    }
}
