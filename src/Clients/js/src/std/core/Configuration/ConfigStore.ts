import { assertArgument, PublicCtor, TimeSpan } from '../..';
import { Address, ServiceId } from '..';
import { ConfigBuilder, ConnectHelper } from '.';
import { ConfigCell } from './ConfigCell';

/* @internal */
export class ConfigStore {

    public getBuilder<TAddress extends Address>(
        address?: Address | undefined,
        serviceId?: ServiceId | undefined,
    ): ConfigBuilder<TAddress> {
        assertArgument({ address }, Address, 'undefined');
        assertArgument({ serviceId }, ServiceId, 'undefined');

        const compositeKey = ConfigStore.computeCompositeKey(address, serviceId);

        let result = this._map.get(compositeKey);

        if (result) {
            return result as unknown as ConfigBuilder<TAddress>;
        }

        result = new ConfigCell();
        this._map.set(compositeKey, result);

        return result as unknown as ConfigBuilder<TAddress>;
    }

    public getConnectHelper<TAddress extends Address>(
        address: TAddress,
    ): ConnectHelper<TAddress> | undefined {
        const key = ConfigStore.computeCompositeKey(address, undefined);

        return this._map.get(key)?.connectHelper;
    }

    public getRequestTimeout<TService, TAddress extends Address>(
        service: PublicCtor<TService>,
        address: TAddress,
    ): TimeSpan | undefined {
        for (const key of enumerateCandidateKeys()) {
            const cell = this._map.get(key);

            const maybeValue = cell?.requestTimeout;

            if (maybeValue) {
                return maybeValue;
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

    private static computeCompositeKey(
        address: Address | undefined,
        serviceOrServiceId: ServiceId | PublicCtor | undefined,
    ) {
        const serviceKey =
            serviceOrServiceId === undefined
                ? ''
                : typeof serviceOrServiceId === 'function'
                    ? serviceOrServiceId.name
                    : serviceOrServiceId.key;

        const compositeKey = `[${address?.key ?? ''}][${serviceKey}]`;
        return compositeKey;
    }

    private readonly _map = new Map<string, ConfigCell>();
}
