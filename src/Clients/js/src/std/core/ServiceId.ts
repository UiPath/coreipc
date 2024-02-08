import { PublicCtor } from '..';

export class ServiceId<TService = unknown> {
    public static from<TService>(service: PublicCtor<TService>) {
        return new ServiceId<TService>(service);
    }

    constructor(
        public readonly service: PublicCtor<TService>,
        public readonly endpointName?: string,
    ) {}

    /* @internal */
    public get key() {
        return this.endpointName ?? this.service.name;
    }
}
