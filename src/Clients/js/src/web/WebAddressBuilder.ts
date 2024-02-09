import { AddressBuilder, PublicCtor } from '../std';
import { BrowserWebSocketAddress } from './Transport';

export class WebAddressBuilder extends AddressBuilder<BrowserWebSocketAddress> {
    /* @internal */
    public assertAddress(): BrowserWebSocketAddress {
        if (this._address instanceof BrowserWebSocketAddress) {
            return this._address;
        }

        throw new Error('Method isWebSocket was not called in the address callback.');
    }

    public isWebSocket(url: string): PublicCtor<BrowserWebSocketAddress> {
        this._address = new BrowserWebSocketAddress(url);
        return BrowserWebSocketAddress;
    }
}
