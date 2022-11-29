import { assertArgument, PublicCtor, TimeSpan } from '../..';
import { Address, AddressBuilder, IServiceProvider } from '..';
import { ConnectHelper } from '.';

/* @internal */
export class ConfigStore<TAddressBuilder extends AddressBuilder> {
    private static readonly EmptyKey = '';

    constructor(
        private readonly _serviceProvider: IServiceProvider<TAddressBuilder>,
    ) {}

    public getConnectHelper<TAddress extends Address>(
        address: TAddress,
    ): ConnectHelper<TAddress> | undefined {
        const key = ConfigStore.computeCompositeKey<
            TAddressBuilder,
            TAddress,
            any
        >(this._serviceProvider, address, undefined);

        return (
            this._connectHelpers.get(key) ??
            this._connectHelpers.get(ConfigStore.EmptyKey)
        );
    }

    public setConnectHelper<TAddress extends Address>(
        address: TAddress | undefined,
        value: ConnectHelper<TAddress> | undefined,
    ): void {
        assertArgument({ address }, Address, 'undefined');
        assertArgument({ value }, 'function', 'undefined');

        const key = address?.key ?? ConfigStore.EmptyKey;

        if (!value) {
            this._connectHelpers.delete(key);
            return;
        }

        this._connectHelpers.set(key, value);
    }

    public getRequestTimeout<TService, TAddress extends Address>(
        address: TAddress,
        service: PublicCtor<TService>,
    ): TimeSpan | undefined {
        for (const key of enumerateCandidateKeys(this._serviceProvider)) {
            const result = this._requestTimeouts.get(key);
            if (result) {
                return result;
            }
        }

        return undefined;

        function* enumerateCandidateKeys(
            serviceProvider: IServiceProvider<TAddressBuilder>,
        ) {
            function make(
                address: TAddress | undefined,
                service: PublicCtor<TService> | undefined,
            ): string {
                return ConfigStore.computeCompositeKey<
                    TAddressBuilder,
                    TAddress,
                    TService
                >(serviceProvider, address, service);
            }

            yield make(address, service);
            yield make(address, undefined);
            yield make(undefined, service);
            yield make(undefined, undefined);
        }
    }

    public setRequestTimeout<TService, TAddress extends Address>(
        address: TAddress | undefined,
        service: PublicCtor<TService> | undefined,
        value: TimeSpan | undefined,
    ): void {
        assertArgument({ address }, Address, 'undefined');
        assertArgument({ service }, 'function', 'undefined');
        assertArgument({ value }, TimeSpan, 'undefined');

        const key = ConfigStore.computeCompositeKey<
            TAddressBuilder,
            TAddress,
            TService
        >(this._serviceProvider, address, service);

        if (!value) {
            this._requestTimeouts.delete(key);
            return;
        }

        this._requestTimeouts.set(key, value);
    }

    private static computeCompositeKey<
        TAddressBuilder extends AddressBuilder,
        TAddress extends Address,
        TService,
    >(
        serviceProvider: IServiceProvider<TAddressBuilder>,
        address: TAddress | undefined,
        service: PublicCtor<TService> | undefined,
    ) {
        const addressKey = address?.key ?? ConfigStore.EmptyKey;

        const serviceKey = !service
            ? ConfigStore.EmptyKey
            : serviceProvider.contractStore.maybeGet<TService>(service)
                  ?.endpoint ?? service.name;

        const compositeKey = `[${addressKey}][${serviceKey}]`;
        return compositeKey;
    }

    private readonly _requestTimeouts = new Map<string, TimeSpan>();

    private readonly _connectHelpers = new Map<string, ConnectHelper<any>>();
}
