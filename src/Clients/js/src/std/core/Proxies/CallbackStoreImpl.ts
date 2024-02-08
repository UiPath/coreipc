import { Address } from '../Addresses';

/* @internal */
export class CallbackStoreImpl {
    public get(callbackEndpointName: string, address: Address): any | undefined {
        const compositeKey = CallbackStoreImpl.computeCompositeKey(callbackEndpointName, address);

        return this._map.get(compositeKey);
    }

    public set(callbackEndpointName: string, address: Address, instance: any): void {
        const compositeKey = CallbackStoreImpl.computeCompositeKey(callbackEndpointName, address);

        this._map.set(compositeKey, instance);
    }

    private static computeCompositeKey(callbackEndpointName: string, address: Address): string {
        return `${callbackEndpointName}@${address.key}`;
    }

    private readonly _map = new Map<string, any>();
}
