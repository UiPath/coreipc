import { IIpc } from '../IIpc';
import { PublicCtor, argumentIs } from '@foundation';

/* @internal */
export class CallbackStore implements IIpc.CallbackStoreInternal {
    public set<TCallback>(callbackType: PublicCtor<TCallback>, pipeName: string, callback: TCallback): void;
    public set<TCallback>(callbackEndpointName: string, pipeName: string, callback: TCallback): void;
    public set<TCallback>(arg0: PublicCtor<TCallback> | string, pipeName: string, callback: TCallback) {
        argumentIs(arg0, 'arg0', 'string', 'function');

        if (typeof arg0 === 'function') { arg0 = arg0.name; }

        this._map.get(pipeName).set(arg0, callback);
    }

    public get<TCallback = unknown>(callbackEndpointName: string, pipeName: string): TCallback | undefined {
        return this._map.get(pipeName).get<TCallback>(callbackEndpointName);
    }

    private readonly _map = new PipeNameToInnerMap();
}

class EndpointNameToCallback {
    private readonly _map = new Map<string, unknown>();

    public set<TCallback>(callbackEndpointName: string, callback: TCallback): void { this._map.set(callbackEndpointName, callback); }

    public get<TCallback>(callbackEndpointName: string): TCallback | undefined {
        return this._map.get(callbackEndpointName) as TCallback | undefined;
    }
}

class PipeNameToInnerMap {
    public get(pipeName: string): EndpointNameToCallback {
        let result = this._map.get(pipeName);
        if (!result) { result = this.add(pipeName); }
        return result;
    }

    private readonly _map = new Map<string, EndpointNameToCallback>();

    private add(pipeName: string): EndpointNameToCallback {
        const result = new EndpointNameToCallback();
        this._map.set(pipeName, result);
        return result;
    }
}
