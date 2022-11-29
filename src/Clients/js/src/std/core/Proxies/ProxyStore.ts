import { PublicCtor } from '../../bcl';
import { Address, AddressBuilder } from '..';
import { Accessor, IServiceProvider, ProxyId, ProxyManager } from '.';

/* @internal */
export class ProxyStore<TAddressBuilder extends AddressBuilder> {
    constructor(
        private readonly _serviceProvider: IServiceProvider<TAddressBuilder>,
    ) {}

    public resolve<TService, TAddress extends Address = Address>(
        proxyId: ProxyId<TService, TAddress>,
    ): TService {
        const attachment = ServiceAttachment.getOrCreate(
            this._serviceProvider,
            proxyId.service,
        );

        const proxyManager = attachment.getOrCreate(proxyId.address);

        return proxyManager.proxy;
    }
}

class ServiceAttachment<TAddressBuilder extends AddressBuilder, TService> {
    public static getOrCreate<TAddressBuilder extends AddressBuilder, TService>(
        serviceProvider: IServiceProvider<TAddressBuilder>,
        service: PublicCtor<TService>,
    ): ServiceAttachment<TAddressBuilder, TService> {
        const container = service as any as ServiceAttachmentContainer<
            TAddressBuilder,
            TService
        >;

        const accessor = Accessor.of<
            ServiceAttachment<TAddressBuilder, TService>
        >().from(service, serviceProvider.symbolofServiceAttachment);

        return (accessor.value ??= new ServiceAttachment<
            TAddressBuilder,
            TService
        >(serviceProvider, service));
    }

    public getOrCreate<TAddress extends Address>(
        address: TAddress,
    ): ProxyManager<TAddressBuilder, TService, TAddress> {
        let result = this._proxyManagers.get(address.key);

        if (!result) {
            result = new ProxyManager<TAddressBuilder, TService, TAddress>(
                this._serviceProvider,
                new ProxyId<TService, TAddress>(this._service, address),
            );
        }

        return result;
    }

    private constructor(
        private readonly _serviceProvider: IServiceProvider<TAddressBuilder>,
        private readonly _service: PublicCtor<TService>,
    ) {}

    private readonly _proxyManagers = new Map<
        string,
        ProxyManager<any, any, any>
    >();
}

interface ServiceAttachmentContainer<
    TAddressBuilder extends AddressBuilder,
    TService,
> {
    [key: symbol]: ServiceAttachment<TAddressBuilder, TService> | undefined;
}
