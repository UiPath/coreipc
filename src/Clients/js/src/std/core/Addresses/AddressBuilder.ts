import { Address } from '.';

export abstract class AddressBuilder<TAddress extends Address = Address> {
    /* @internal */
    public abstract assertAddress(): Address;

    protected _address: TAddress | undefined;
}
