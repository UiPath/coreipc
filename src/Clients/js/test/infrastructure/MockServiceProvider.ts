import {
    AddressBuilder,
    CallbackStoreImpl,
    ChannelManagerStore,
    ConfigStore,
    DispatchProxyStore,
    IContractStore,
    IServiceProvider,
    ProxyStore,
} from '../../src/std';

export class MockServiceProvider<TAddressBuilder extends AddressBuilder>
    implements IServiceProvider<TAddressBuilder>
{
    constructor(
        private readonly _params?: {
            addressBuilderCtor?: { new (): TAddressBuilder };
            implementation?: Partial<IServiceProvider<TAddressBuilder>>;
        },
    ) {}

    createAddressBuilder(): TAddressBuilder {
        if (this._params?.implementation?.createAddressBuilder) {
            return this._params.implementation.createAddressBuilder();
        }

        if (this._params?.addressBuilderCtor) {
            return new this._params.addressBuilderCtor();
        }

        throw new Error('Method not implemented.');
    }

    get symbolofServiceAttachment(): symbol {
        return this.read('symbolofServiceAttachment');
    }
    get configStore(): ConfigStore<any> {
        return this.read('configStore');
    }
    get proxyStore(): ProxyStore<any> {
        return this.read('proxyStore');
    }
    get dispatchProxyStore(): DispatchProxyStore {
        return this.read('dispatchProxyStore');
    }
    get channelStore(): ChannelManagerStore {
        return this.read('channelStore');
    }
    get contractStore(): IContractStore {
        return this.read('contractStore');
    }
    get callbackStore(): CallbackStoreImpl {
        return this.read('callbackStore');
    }

    private read<Key extends keyof IServiceProvider<TAddressBuilder>>(
        key: Key,
    ): IServiceProvider<TAddressBuilder>[Key] {
        if (this._params?.implementation) {
            const result = this._params.implementation[key];
            if (result !== undefined) {
                return result;
            }
        }

        throw new Error('Property not implemented.');
    }
}
