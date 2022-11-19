import { AddressBuilder, PublicCtor } from '../std';
import { NamedPipeAddress, NodeWebSocketAddress } from './Transport';

/* @internal */
export class NodeAddressBuilder extends AddressBuilder {
    public isPipe(name: string): PublicCtor<NamedPipeAddress> {
        super._address = new NamedPipeAddress(name);
        return NamedPipeAddress;
    }

    public isWebSocket(url: string): PublicCtor<NodeWebSocketAddress> {
        super._address = new NodeWebSocketAddress(url);
        return NodeWebSocketAddress;
    }
}
