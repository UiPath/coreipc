import { PublicCtor } from '../../bcl';
import { Address } from '..';

/* @internal */
export class ProxyId<TService, TAddress extends Address> {
    constructor(
        public readonly service: PublicCtor<TService>,
        public readonly address: TAddress,
    ) {}
}
