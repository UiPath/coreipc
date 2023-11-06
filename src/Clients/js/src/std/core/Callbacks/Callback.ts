import { PublicCtor } from '../..';
import { AddressBuilder, AddressSelectionDelegate } from '../Addresses';

export interface Callback<TAddressBuilder extends AddressBuilder> {
    forAddress(configure: AddressSelectionDelegate<TAddressBuilder>): CallbackForAddress;
}

export interface CallbackForAddress {
    forService<TService>(callbackService: PublicCtor<TService>): CallbackForAddressService<TService>;
    forService<TService>(callbackEndpointName: string): CallbackForAddressService<TService>;
}

export interface CallbackForAddressService<TService> {
    is(instance: TService): void;
}
