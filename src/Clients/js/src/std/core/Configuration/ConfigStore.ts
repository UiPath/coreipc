import { assertArgument, PublicCtor, TimeSpan } from '../..';
import { Address, AddressBuilder, IServiceProvider } from '..';
import { ConnectHelper } from '.';
import { TelemetryPropagationPolicy } from '../Telemetry';

/* @internal */
export class ConfigStore<TAddressBuilder extends AddressBuilder> {
    private static readonly EmptyKey = '';

    constructor(
        private readonly _serviceProvider: IServiceProvider<TAddressBuilder>,
    ) {}

    public getConnectHelper(address: Address): ConnectHelper | undefined {
        return this.getForAddress(this._connectHelpers, address);
    }

    public setConnectHelper(
        address: Address | undefined,
        value: ConnectHelper | undefined,
    ): void {
        assertArgument({ address }, Address, 'undefined');
        assertArgument({ value }, 'function', 'undefined');

        const key = this.addressKey(address);

        if (!value) {
            this._connectHelpers.delete(key);
            return;
        }

        this._connectHelpers.set(key, value);
    }

    public getTelemetryPropagation(address: Address): TelemetryPropagationPolicy {
        return this.getForAddress(this._telemetryPropagations, address) ?? TelemetryPropagationPolicy.None;
    }

    public setTelemetryPropagation(
        address: Address | undefined,
        value: TelemetryPropagationPolicy,
    ): void {
        assertArgument({ address }, Address, 'undefined');

        this._telemetryPropagations.set(this.addressKey(address), value);
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

    private addressKey(address?: Address): string {
        return !address
            ? ConfigStore.EmptyKey
            : ConfigStore.computeCompositeKey(this._serviceProvider, address);
    }

    private getForAddress<T>(map: Map<string, T>, address: Address): T | undefined {
        return map.get(this.addressKey(address)) ?? map.get(ConfigStore.EmptyKey);
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

    private readonly _telemetryPropagations = new Map<string, TelemetryPropagationPolicy>();
}
