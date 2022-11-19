import { Address } from '../Addresses';

/* @internal */
export class CallbackStoreImpl {
    public get<TAddress extends Address, TService = any>(
        callbackEndpointName: string,
        address: TAddress,
    ): TService | undefined {
        const compositeKey = CallbackStoreImpl.computeCompositeKey(callbackEndpointName, address);

        return this._map.get(compositeKey);
    }

    public set<TAddress extends Address, TService = any>(
        callbackEndpointName: string,
        address: TAddress,
        instance: TService,
    ): void {
        const compositeKey = CallbackStoreImpl.computeCompositeKey(callbackEndpointName, address);

        this._map.set(compositeKey, instance);
    }

    private static computeCompositeKey<TAddress extends Address>(callbackEndpointName: string, address: TAddress): string {
        return `${callbackEndpointName}@${address.key}`;
    }

    private readonly _map = new Map<string, any>();
}
