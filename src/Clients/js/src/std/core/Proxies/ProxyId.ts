import { PublicCtor } from '../../bcl';
import { Address } from '..';

/* @internal */
export class ProxyId<TService = unknown> {
    constructor(
        public readonly service: PublicCtor<TService>,
        public readonly address: Address,
    ) {}
}
