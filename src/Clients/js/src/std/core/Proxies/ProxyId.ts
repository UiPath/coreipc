import { PublicCtor } from '../../bcl';
import { Address } from '..';

/* @internal */
export class ProxyId<TService = unknown, TAddress extends Address = Address> {
    constructor(
        public readonly service: PublicCtor<TService>,
        public readonly address: TAddress,
    ) {}
}
