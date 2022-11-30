import { AddressBuilder, PublicCtor } from '../std';
import { BrowserWebSocketAddress } from './Transport';

export class WebAddressBuilder extends AddressBuilder {
    public isWebSocket(url: string): PublicCtor<BrowserWebSocketAddress> {
        super._address = new BrowserWebSocketAddress(url);
        return BrowserWebSocketAddress;
    }
}
