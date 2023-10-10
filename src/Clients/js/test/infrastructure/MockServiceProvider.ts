import {
    AddressBuilder,
    CallbackStoreImpl,
    Wire,
    ConfigStore,
    DispatchProxyClassStore,
    IContractStore,
    IServiceProvider,
    ProxySource,
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

    get configStore(): ConfigStore<any> {
        return this.read('configStore');
    }
    get proxySource(): ProxySource<any> {
        return this.read('proxySource');
    }
    get dispatchProxies(): DispatchProxyClassStore {
        return this.read('dispatchProxies');
    }
    get wire(): Wire {
        return this.read('wire');
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
