import { AddressBuilder } from '../std';
import { NamedPipeAddress, NodeWebSocketAddress } from './Transport';

export class NodeAddressBuilder extends AddressBuilder<NamedPipeAddress | NodeWebSocketAddress> {
    public isPipe(name: string): void {
        this._address = new NamedPipeAddress(name);
    }

    public isWebSocket(url: string): void {
        this._address = new NodeWebSocketAddress(url);
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
