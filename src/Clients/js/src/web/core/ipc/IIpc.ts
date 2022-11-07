import { IIpcStandard } from "@core";
import { argumentIs, IAddress, PublicCtor, WebSocketAddress } from "@foundation";

export import MethodAnnotations = IIpcStandard.MethodAnnotations;
export import ClassAnnotations = IIpcStandard.ClassAnnotations;
export import Config = IIpcStandard.ConfigStore.Writer;

export interface IIpc {
    readonly $operation: MethodAnnotations;
    readonly $service: ClassAnnotations;
    readonly config: Config;
    readonly callback: Callback;
    readonly proxy: Proxy;
}

export interface Callback {
    forWebSocket(url: string | URL): Callback.ForTransport;
}

export interface Proxy {
    forWebSocket(url: string | URL): Proxy.ForTransport;
}

export module Callback {
    export interface ForTransport {
        andCallbackType<T>(type: PublicCtor<T>): Cell<T>;
        andEndpointName<T = any>(name: string): Cell<T>;
    }

    export interface Cell<T = any> {
        is(instance: T): void;
    }
}

export module Proxy {
    export interface ForTransport {
        get<T>(service: PublicCtor<T>): T;
    }
}

/* @internal */
export class IpcImpl implements IIpc {
    constructor(public readonly target: IIpcStandard) { }

    get $operation(): MethodAnnotations { return this.target.$operation; }
    get $service(): ClassAnnotations { return this.target.$service; }
    get config(): Config { return this.target.config; }

    readonly callback: Callback = new CallbackImpl(this);
    readonly proxy: Proxy = new ProxyImpl(this);
}

/* @internal */
export class CallbackImpl implements Callback {
    constructor(private readonly _ipc: IpcImpl) { }

    forWebSocket(url: string | URL): Callback.ForTransport {
        argumentIs(url, 'url', 'string', URL);

        if (url instanceof URL) {
            url = url.toString();
        }

        return new CallbackForTransportImpl(
            this._ipc,
            new WebSocketAddress(url));
    }
}

/* @internal */
export class CallbackForTransportImpl implements Callback.ForTransport {
    constructor(
        private readonly _ipc: IpcImpl,
        private readonly _address: IAddress) { }

    andCallbackType<T>(type: PublicCtor<T>): Callback.Cell<T> {
        argumentIs(type, 'type', 'function');

        return new CellImpl<T>(
            this._ipc,
            this._address,
            type);
    }
    andEndpointName<T = any>(name: string): Callback.Cell<T> {
        argumentIs(name, 'name', 'string');

        return new CellImpl<T>(
            this._ipc,
            this._address,
            name);
    }
}

/* @internal */
export class CellImpl<T = any> implements Callback.Cell<T> {
    constructor(
        private readonly _ipc: IpcImpl,
        private readonly _address: IAddress,
        private readonly _typeOrName: PublicCtor<T> | string) { }


    is(instance: T): void {
        this._ipc
            .target
            .callback
            .set(
                this._typeOrName as any,
                this._address,
                instance);
    }
}

/* @internal */
export class ProxyImpl implements Proxy {
    constructor(private readonly _ipc: IpcImpl) { }

    forWebSocket(url: string | URL): Proxy.ForTransport {
        argumentIs(url, 'url', 'string', URL);
        if (url instanceof URL) {
            url = url.toString();
        }
        return new ProxyForTransportImpl(this._ipc, new WebSocketAddress(url));
    }
}

/* @internal */
export class ProxyForTransportImpl implements Proxy.ForTransport {
    constructor(
        private readonly _ipc: IpcImpl,
        private readonly _address: IAddress) { }

    get<T>(service: PublicCtor<T>): T {
        return this._ipc
            .target
            .proxy
            .get(
                this._address,
                service);
    }
}
