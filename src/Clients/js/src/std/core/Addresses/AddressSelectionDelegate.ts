export interface AddressSelectionDelegate<TAddressFactory> {
    (addressBuilder: TAddressFactory): void;
}
