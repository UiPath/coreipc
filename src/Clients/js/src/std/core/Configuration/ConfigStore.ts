import { assertArgument, TimeSpan } from '../..';
import { Address, ServiceId } from '..';
import { ConnectHelper } from '.';

/* @internal */
export class ConfigStore {
    private static readonly EmptyKey = '';

    public getConnectHelper<TAddress extends Address>(
        address: TAddress,
    ): ConnectHelper<TAddress> | undefined {
        const key = ConfigStore.computeCompositeKey(address, undefined);

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
        service: ServiceId<TService>,
    ): TimeSpan | undefined {
        for (const key of enumerateCandidateKeys()) {
            const result = this._requestTimeouts.get(key);
            if (result) {
                return result;
            }
        }

        return undefined;

        function* enumerateCandidateKeys() {
            yield ConfigStore.computeCompositeKey(address, service);
            yield ConfigStore.computeCompositeKey(address, undefined);
            yield ConfigStore.computeCompositeKey(undefined, service);
            yield ConfigStore.computeCompositeKey(undefined, undefined);
        }
    }

    public setRequestTimeout<TService, TAddress extends Address>(
        address: TAddress | undefined,
        service: ServiceId<TService> | undefined,
        value: TimeSpan | undefined,
    ): void {
        assertArgument({ address }, Address, 'undefined');
        assertArgument({ service }, ServiceId, 'undefined');
        assertArgument({ value }, TimeSpan, 'undefined');

        const key = ConfigStore.computeCompositeKey(address, service);

        if (!value) {
            this._requestTimeouts.delete(key);
            return;
        }

        this._requestTimeouts.set(key, value);
    }

    private static computeCompositeKey(
        address: Address | undefined,
        serviceOrServiceId: ServiceId | undefined,
    ) {
        const addressKey = address?.key ?? ConfigStore.EmptyKey;

        const serviceKey =
            serviceOrServiceId?.endpointName ??
            serviceOrServiceId?.key ??
            ConfigStore.EmptyKey;

        const compositeKey = `[${addressKey}][${serviceKey}]`;
        return compositeKey;
    }

    private readonly _requestTimeouts = new Map<string, TimeSpan>();

    private readonly _connectHelpers = new Map<string, ConnectHelper<any>>();
}
