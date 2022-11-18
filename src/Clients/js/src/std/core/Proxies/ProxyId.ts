import { ServiceId, Address } from '..';

/* @internal */
export class ProxyId<TService, TAddress extends Address> {
    constructor(
        public readonly serviceId: ServiceId<TService>,
        public readonly address: TAddress,
    ) {}

    public get key(): string {
        return `${this.serviceId.key}@${this.address}`;
    }
}
