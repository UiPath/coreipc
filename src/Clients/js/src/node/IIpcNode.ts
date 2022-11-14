import {
    IIpc,
    IProxy,
    Proxy,
    IProxyWithAddress,
    ProxyWithAddress,
    Address,
    WebSocketAddress,
    PublicCtor,
} from '../std';

/* @internal */
export class NamedPipeAddress extends Address {
    constructor(public readonly pipeName: string) {
        super();
    }
}

export interface IProxyNode extends IProxy {
    withNamedPipe(name: string): IProxyWithAddress;
}

export interface IIpcNode extends IIpc {
    readonly proxy: IProxyNode;
}

/* @internal */
export class IpcNode implements IIpcNode {
    get proxy(): IProxyNode {
        return new ProxyNode();
    }
}

/* @internal */
export class ProxyNode extends Proxy implements IProxyNode {
    withNamedPipe(name: string): IProxyWithAddress {
        const address = new NamedPipeAddress(name);

        return new ProxyWithAddressNode(address);
    }
    override withWebSocket(url: string): IProxyWithAddress {
        const address = new WebSocketAddress(url);

        return new ProxyWithAddressNode(address);
    }
}

/* @internal */
export class ProxyWithAddressNode extends ProxyWithAddress {
    constructor(address: Address) {
        super(address);
    }

    withService<TService>(ctor: PublicCtor<TService>, endpointName?: string): TService {
        const result = {
            address: this.address,
            ctor,
            endpointName,
            createdBy: ProxyWithAddressNode,
        } as any;

        return result;
    }
}

export const ipc = new IpcNode();
