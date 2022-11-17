import { AddressBuilder as AddressBuilderBase, PublicCtor } from '../std';
import { BrowserWebSocketAddress } from '.';

export class AddressBuilder extends AddressBuilderBase {
    public isWebSocket(url: string): PublicCtor<BrowserWebSocketAddress> {
        super._address = new BrowserWebSocketAddress(url);
        return BrowserWebSocketAddress;
    }
}
