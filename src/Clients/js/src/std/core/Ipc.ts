import { PublicCtor, ParameterlessPublicCtor } from '../bcl';

import {
    Address,
    AddressBuilder,
    ServiceId,
    UpdateConfigDelegate,
    AddressSelectionDelegate,
    ConfigStore,
    BrowserWebSocketAddress,
} from '.';

export abstract class Ipc<TAddressBuilder extends AddressBuilder = any> {
    constructor(
        /* @internal */
        public readonly channelSelectorCtor: ParameterlessPublicCtor<TAddressBuilder>
    ) {}

    public webSocket(
        url: string
    ): AddressSelectionDelegate<TAddressBuilder, BrowserWebSocketAddress> {
        return (builder) => builder.isWebSocket(url);
    }

    public readonly proxy: Ipc.ProxySource<TAddressBuilder> =
        new Ipc.ProxySource<TAddressBuilder>(this);

    public readonly config: Ipc.Configuration<TAddressBuilder> =
        new Ipc.Configuration<TAddressBuilder>(this);

    /* @internal */
    public readonly configStore: ConfigStore = new ConfigStore();
}

export module Ipc {
    export class ProxySource<TAddressBuilder extends AddressBuilder> {
        /* @internal */
        constructor(private readonly _ipc: Ipc<TAddressBuilder>) {}

        public withAddress<TAddress extends Address>(
            configure: AddressSelectionDelegate<TAddressBuilder, TAddress>
        ): ProxySourceWithAddress {
            const builder = new this._ipc.channelSelectorCtor();
            const type = configure(builder);
            const address = builder.assertAddress<TAddress>(type);

            return new ProxySourceWithAddress(this._ipc, address);
        }
    }

    export class ProxySourceWithAddress {
        /* @internal */
        constructor(
            private readonly _ipc: Ipc,
            private readonly _address: Address
        ) {}

        public withService<TService>(
            ctor: PublicCtor<TService>,
            endpointName?: string
        ): TService {
            return {} as unknown as TService;
        }
    }

    export class Configuration<TAddressBuilder extends AddressBuilder> {
        /* @internal */
        constructor(private readonly _ipc: Ipc<TAddressBuilder>) {}

        public forAnyAddress(): ConfigurationWithAddress {
            return new ConfigurationWithAddress(this._ipc);
        }

        public forAddress<TAddress extends Address>(
            configure: AddressSelectionDelegate<TAddressBuilder, TAddress>
        ): ConfigurationWithAddress<TAddress> {
            const builder = new this._ipc.channelSelectorCtor();
            const type = configure(builder);
            const address = builder.assertAddress<TAddress>(type);

            return new ConfigurationWithAddress(this._ipc, address);
        }
    }

    export class ConfigurationWithAddress<TAddress extends Address = Address> {
        /* @internal */
        constructor(
            private readonly _ipc: Ipc,
            private readonly _address?: TAddress
        ) {}

        public forAnyService(): ConfigurationWithAddressService<TAddress> {
            return new ConfigurationWithAddressService(
                this._ipc,
                this._address
            );
        }
    }

    export class ConfigurationWithAddressService<
        TAddress extends Address = Address
    > {
        /* @internal */
        constructor(
            private readonly _ipc: Ipc,
            private readonly _address: TAddress | undefined,
            private readonly _serviceId?: ServiceId
        ) {}

        public update(updateAction: UpdateConfigDelegate<TAddress>) {
            const builder = this._ipc.configStore.getBuilder<TAddress>(
                this._address,
                this._serviceId
            );

            updateAction(builder);
        }
    }
}
