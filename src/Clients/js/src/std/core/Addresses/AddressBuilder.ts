import { PublicCtor } from '../../bcl';
import { Address } from '.';

export class AddressBuilder {
    /* @internal */
    public assertAddress<TAddress extends Address>(type: PublicCtor<TAddress>): TAddress {
        if (!(this._address instanceof type)) {
            throw new Error();
        }

        return this._address;
    }

    protected _address: Address | undefined;
}
