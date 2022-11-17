import { AddressBuilder as AddressBuilderBase, PublicCtor } from '../std';
import { NamedPipeAddress, NodeWebSocketAddress } from '.';

export class AddressBuilder extends AddressBuilderBase {
    public isPipe(name: string): PublicCtor<NamedPipeAddress> {
        super._address = new NamedPipeAddress(name);
        return NamedPipeAddress;
    }

    public isWebSocket(url: string): PublicCtor<NodeWebSocketAddress> {
        super._address = new NodeWebSocketAddress(url);
        return NodeWebSocketAddress;
    }
}
