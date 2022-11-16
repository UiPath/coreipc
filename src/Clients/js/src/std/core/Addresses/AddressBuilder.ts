import { PublicCtor } from '../../bcl';
import { Address, BrowserWebSocketAddress } from '.';

export class AddressBuilder {
    public isWebSocket(url: string): PublicCtor<BrowserWebSocketAddress> {
        this._address = new BrowserWebSocketAddress(url);

        return BrowserWebSocketAddress;
    }

    /* @internal */
    public assertAddress<TAddress extends Address>(type: PublicCtor<TAddress>): TAddress {
        if (!(this._address instanceof type)) {
            throw new Error();
        }

        return this._address;
    }

    protected _address: Address | undefined;
}
