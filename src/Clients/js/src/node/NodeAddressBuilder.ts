import { Address, AddressBuilder, PublicCtor } from '../std';
import { NamedPipeAddress, NodeWebSocketAddress } from './Transport';

export class NodeAddressBuilder extends AddressBuilder<NamedPipeAddress | NodeWebSocketAddress> {
    public isPipe(name: string): void {
        super._address = new NamedPipeAddress(name);
    }

    public isWebSocket(url: string): void {
        super._address = new NodeWebSocketAddress(url);
    }

    /* @internal */
    public assertAddress(): NamedPipeAddress | NodeWebSocketAddress {
        if (this._address instanceof NamedPipeAddress ||
            this._address instanceof NodeWebSocketAddress) {
            return this._address;
        }

        throw new Error('Neither method isPipe nor isWebSocket was called in the address callback.');
    }
}
