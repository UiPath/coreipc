import { assertArgument, PublicCtor } from '../../bcl';
import { Address, AddressBuilder, AddressSelectionDelegate } from '../Addresses';
import { IServiceProvider } from '../IServiceProvider';
import { Callback, CallbackForAddress, CallbackForAddressService } from './Callback';

/* @internal */
export class CallbackImpl<TAddressBuilder extends AddressBuilder>
    implements Callback<TAddressBuilder>
{
    constructor(public readonly _serviceProvider: IServiceProvider<TAddressBuilder>) {}

    forAddress<TAddress extends Address>(
        configure: AddressSelectionDelegate<TAddressBuilder, TAddress>,
    ): CallbackForAddress {
        const builder = this._serviceProvider.createAddressBuilder();
        const type = configure(builder);
        const address = builder.assertAddress<TAddress>(type);

        return new CallbackForAddressImpl<TAddress>(this._serviceProvider, address);
    }
}

/* @internal */
export class CallbackForAddressImpl<TAddress extends Address> implements CallbackForAddress {
    constructor(
        private readonly _serviceProvider: IServiceProvider,
        private readonly _address: TAddress,
    ) {}

    forService<TService>(
        callbackService: PublicCtor<TService>,
    ): CallbackForAddressService<TService>;
    forService<TService>(callbackEndpointName: string): CallbackForAddressService<TService>;
    forService<TService>(
        callbackServiceOrEndpointName: PublicCtor<TService> | string,
    ): CallbackForAddressService<TService> {
        assertArgument({ callbackServiceOrEndpointName }, 'function', 'string');

        if (typeof callbackServiceOrEndpointName === 'function') {
            callbackServiceOrEndpointName = callbackServiceOrEndpointName.name;
        }

        return new CallbackForAddressServiceImpl<TService, TAddress>(
            this._serviceProvider,
            this._address,
            callbackServiceOrEndpointName,
        );
    }
}

/* @internal */
export class CallbackForAddressServiceImpl<TService, TAddress extends Address>
    implements CallbackForAddressService<TService>
{
    constructor(
        private readonly _serviceProvider: IServiceProvider,
        private readonly _address: TAddress,
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
