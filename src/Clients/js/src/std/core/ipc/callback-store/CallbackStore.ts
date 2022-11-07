import { PublicCtor, argumentIs, IAddress, ArgumentOutOfRangeError } from '@foundation';
import { IIpcStandard } from '../IIpc';

/* @internal */
export class CallbackStore implements IIpcStandard.CallbackStoreInternal {
    public set<TCallback>(callbackType: PublicCtor<TCallback>, address: IAddress, callback: TCallback): void;
    public set<TCallback>(callbackEndpointName: string, address: IAddress, callback: TCallback): void;
    public set<TCallback>(arg0: PublicCtor<TCallback> | string, address: IAddress, callback: TCallback) {
        argumentIs(arg0, 'arg0', 'string', 'function');

        if (typeof arg0 === 'function') { arg0 = arg0.name; }

        if (!arg0) { throw new ArgumentOutOfRangeError('arg0', 'The function name must cannot be null or undefined.'); }

        this._map.get(address).set(arg0, callback);
    }

    public get<TCallback = unknown>(callbackEndpointName: string, address: IAddress): TCallback | undefined {
        return this._map.get(address).get<TCallback>(callbackEndpointName);
    }

    private readonly _map = new AddressToInnerMap();
}

class EndpointNameToCallback {
    private readonly _map = new Map<string, unknown>();

    public set<TCallback>(callbackEndpointName: string, callback: TCallback): void { this._map.set(callbackEndpointName, callback); }

    public get<TCallback>(callbackEndpointName: string): TCallback | undefined {
        return this._map.get(callbackEndpointName) as TCallback | undefined;
    }
}

class AddressToInnerMap {
    public get(address: IAddress): EndpointNameToCallback {
        let result = this._map.get(address.key);
        if (!result) { result = this.add(address); }
        return result;
    }

    private readonly _map = new Map<string, EndpointNameToCallback>();

    private add(address: IAddress): EndpointNameToCallback {
        const result = new EndpointNameToCallback();
        this._map.set(address.key, result);
        return result;
    }
}
