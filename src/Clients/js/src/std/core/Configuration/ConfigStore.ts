import { assertArgument, PublicCtor, TimeSpan } from '../..';
import { Address, AddressBuilder, IServiceProvider } from '..';
import { ConnectHelper } from '.';

/* @internal */
export class ConfigStore<TAddressBuilder extends AddressBuilder> {
    private static readonly EmptyKey = '';

    constructor(
        private readonly _serviceProvider: IServiceProvider<TAddressBuilder>,
    ) {}

    public getConnectHelper(address: Address): ConnectHelper | undefined {
        const key = ConfigStore.computeCompositeKey(this._serviceProvider, address, undefined);

        return (
            this._connectHelpers.get(key) ??
            this._connectHelpers.get(ConfigStore.EmptyKey)
        );
    }

    public setConnectHelper(
        address: Address | undefined,
        value: ConnectHelper | undefined,
    ): void {
        assertArgument({ address }, Address, 'undefined');
        assertArgument({ value }, 'function', 'undefined');

        const key = (!address
            ? ConfigStore.EmptyKey
            : ConfigStore.computeCompositeKey(this._serviceProvider, address));

        if (!value) {
            this._connectHelpers.delete(key);
            return;
        }

        this._connectHelpers.set(key, value);
    }

    public getRequestTimeout(address: Address, service: PublicCtor): TimeSpan | undefined {
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
                address: Address | undefined,
                service: PublicCtor | undefined,
            ): string {
                return ConfigStore.computeCompositeKey(serviceProvider, address, service);
            }

            yield make(address, service);
            yield make(address, undefined);
            yield make(undefined, service);
            yield make(undefined, undefined);
        }
    }

    public setRequestTimeout(
        address: Address | undefined,
        service: PublicCtor | undefined,
        value: TimeSpan | undefined,
    ): void {
        assertArgument({ address }, Address, 'undefined');
        assertArgument({ service }, 'function', 'undefined');
        assertArgument({ value }, TimeSpan, 'undefined');

        const key = ConfigStore.computeCompositeKey(this._serviceProvider, address, service);

        if (!value) {
            this._requestTimeouts.delete(key);
            return;
        }

        this._requestTimeouts.set(key, value);
    }

    private static computeCompositeKey(
        serviceProvider: IServiceProvider,
        address: Address | undefined,
        service?: PublicCtor,
    ): string {
        const addressKey = address?.key ?? ConfigStore.EmptyKey;

        const serviceKey = !service
            ? ConfigStore.EmptyKey
            : serviceProvider.contractStore.maybeGet(service)
                  ?.endpoint ?? service.name;

        return `[${addressKey}][${serviceKey}]`;
    }

    private readonly _requestTimeouts = new Map<string, TimeSpan>();

    private readonly _connectHelpers = new Map<string, ConnectHelper<any>>();
}
