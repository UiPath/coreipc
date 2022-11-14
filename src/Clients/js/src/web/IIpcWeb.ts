import {
    IIpc,
    IProxy,
    Proxy,
    IProxyWithAddress,
    ProxyWithAddress,
    WebSocketAddress,
    Address,
    PublicCtor,
} from '../std';

export interface IIpcWeb extends IIpc {
    readonly proxy: IProxy;
}

/* @internal */
export class IpcWeb implements IIpcWeb {
    get proxy(): IProxy {
        return new ProxyWeb();
    }
}

/* @internal */
export class ProxyWeb extends Proxy {
    override withWebSocket(url: string): IProxyWithAddress {
        const address = new WebSocketAddress(url);

        return new ProxyWithAddressWeb(address);
    }
}

/* @internal */
export class ProxyWithAddressWeb extends ProxyWithAddress {
    constructor(address: Address) {
        super(address);
    }

    withService<TService>(ctor: PublicCtor<TService>, endpointName?: string): TService {
        const result = {
            address: this.address,
            ctor,
            endpointName,
            createdBy: ProxyWithAddressWeb,
        } as any;

        return result;
    }
}

export const ipc = new IpcWeb();
