import { argumentIs, IAddress, PublicCtor, WebSocketAddress } from "../../../std/foundation";
import { IIpcStandard } from "@core";
import { CoreIpcPlatform, NamedPipeSocketAddress } from "node/foundation";

export import MethodAnnotations = IIpcStandard.MethodAnnotations;
export import ClassAnnotations = IIpcStandard.ClassAnnotations;
export import Config = IIpcStandard.ConfigStore.Writer;

export interface IIpc {
    readonly $operation: MethodAnnotations;
    readonly $service: ClassAnnotations;
    readonly config: Config;
    readonly callback: Callback;
    readonly proxy: Proxy;

    pipeExists(shortName: string): Promise<boolean>;
}

export interface Callback {
    /**
     * @deprecated This method should not be used anymore.
     * In a future major update of CoreIpc it will be decomissioned.
     *
     * For registering CoreIpc callbacks in relation to a named pipe please use the {@link forPipe} method instead.
     */
    set<T>(callbackType: PublicCtor<T>, pipeName: string, callback: T): void;
    set<T>(callbackEndpointName: string, pipeName: string, callback: T): void;

    forPipe(name: string): Callback.ForTransport;
    forWebSocket(url: string | URL): Callback.ForTransport;
}

export interface Proxy {
    /**
     * @deprecated This method should not be used anymore.
     * In a future major update of CoreIpc it will be decomissioned.
     *
     * For obtaining proxies to remote CoreIpc services in relation to a named pipe please use the {@link forPipe} method instead.
     */
    get<T, TPipeName extends string = string>(pipeName: TPipeName, service: PublicCtor<T>): T;

    forPipe(name: string): Proxy.ForTransport;
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

    pipeExists(shortName: string): Promise<boolean> {
        return CoreIpcPlatform.current.pipeExists(shortName);
    }
}

/* @internal */
export class CallbackImpl implements Callback {
    constructor(private readonly _ipc: IpcImpl) { }

    public set<T>(callbackType: PublicCtor<T>, pipeName: string, callback: T): void;
    public set<T>(callbackEndpointName: string, pipeName: string, callback: T): void;
    public set<T>(arg0: PublicCtor<T> | string, pipeName: string, callback: T): void {
        argumentIs(arg0, 'arg0', 'function', 'string');

        this
            ._ipc
            .target
            .callback
            .set(
                arg0 as any,
                new NamedPipeSocketAddress(pipeName),
                callback);
    }

    forPipe(name: string): Callback.ForTransport {
        argumentIs(name, 'name', 'string');

        return new CallbackForTransportImpl(
            this._ipc,
            new NamedPipeSocketAddress(name));
    }
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

    get<T, TPipeName extends string = string>(pipeName: TPipeName, service: PublicCtor<T>): T {
        argumentIs(pipeName, 'pipeName', 'string');
        argumentIs(service, 'service', 'function');

        return this._ipc
            .target
            .proxy
            .get(
                new NamedPipeSocketAddress(pipeName),
                service);
    }
    forPipe(name: string): Proxy.ForTransport {
        argumentIs(name, 'name', 'string');
        return new ProxyForTransportImpl(this._ipc, new NamedPipeSocketAddress(name));
    }
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
