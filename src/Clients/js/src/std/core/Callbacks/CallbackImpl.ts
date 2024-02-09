import { assertArgument, PublicCtor } from '../../bcl';
import { Address, AddressBuilder, AddressSelectionDelegate } from '../Addresses';
import { IServiceProvider } from '../IServiceProvider';
import { Callback, CallbackForAddress, CallbackForAddressService } from './Callback';

/* @internal */
export class CallbackImpl<TAddressBuilder extends AddressBuilder>
    implements Callback<TAddressBuilder>
{
    constructor(public readonly _serviceProvider: IServiceProvider<TAddressBuilder>) {}

    forAddress(configure: AddressSelectionDelegate<TAddressBuilder>): CallbackForAddress {
        const address = this._serviceProvider.getAddress(configure);

        return new CallbackForAddressImpl(this._serviceProvider, address);
    }
}

/* @internal */
export class CallbackForAddressImpl implements CallbackForAddress {
    constructor(
        private readonly _serviceProvider: IServiceProvider,
        private readonly _address: Address,
    ) {}

    forService<TService>(callbackService: PublicCtor<TService>): CallbackForAddressService<TService>;
    forService<TService>(callbackEndpointName: string): CallbackForAddressService<TService>;
    forService<TService>(callbackServiceOrEndpointName: PublicCtor<TService> | string): CallbackForAddressService<TService> {
        assertArgument({ callbackServiceOrEndpointName }, 'function', 'string');

        if (typeof callbackServiceOrEndpointName === 'function') {
            callbackServiceOrEndpointName = callbackServiceOrEndpointName.name;
        }

        return new CallbackForAddressServiceImpl<TService>(
            this._serviceProvider,
            this._address,
            callbackServiceOrEndpointName,
        );
    }
}

/* @internal */
export class CallbackForAddressServiceImpl<TService>
    implements CallbackForAddressService<TService>
{
    constructor(
        private readonly _serviceProvider: IServiceProvider,
        private readonly _address: Address,
        private readonly _callbackEndpointName: string,
    ) {}

    is(instance: TService): void {
        this._serviceProvider.callbackStore.set(
            this._callbackEndpointName,
            this._address,
            instance,
        );
    }
}
