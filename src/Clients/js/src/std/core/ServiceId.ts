import { PublicCtor } from '..';

export class ServiceId<TService = unknown> {
    constructor(
        public readonly service: PublicCtor<TService>,
        public readonly endpointName?: string
    ) {}

    /* @internal */
    public get key() {
        return this.endpointName ?? this.service.name;
    }
}
