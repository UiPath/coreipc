import { PublicCtor } from './routines';

export interface IIpc {
}

export interface IProxy {
    withWebSocket(url: string): IProxyWithAddress;
}

export interface IProxyWithAddress {
    withService<TService>(ctor: PublicCtor<TService>, endpointName?: string): TService;
}

/* @internal */
export abstract class Address {
}

/* @internal */
export class WebSocketAddress extends Address {
    constructor(public readonly url: string) {
        super();
    }
}

/* @internal */
export abstract class Proxy implements IProxy {
    abstract withWebSocket(url: string): IProxyWithAddress;
}

/* @internal */
export abstract class ProxyWithAddress implements IProxyWithAddress {
    constructor(public readonly address: Address) { }

    abstract withService<TService>(ctor: PublicCtor<TService>, endpointName?: string): TService;
}
