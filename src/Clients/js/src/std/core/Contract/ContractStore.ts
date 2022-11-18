import { PublicCtor } from '../..';
import { IContractStore } from './IContractStore';
import { ServiceDescriptor, ServiceDescriptorImpl } from '.';

/* @internal */
export class ContractStore implements IContractStore {
    getOrCreate<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService> {
        return this._map.get($class) ?? this.add($class);
    }

    maybeGet<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService> | undefined {
        return this._map.get($class);
    }

    private add<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService> {
        const descriptor = new ServiceDescriptorImpl($class);
        this._map.set($class, descriptor);
        return descriptor;
    }

    private readonly _map = new Map<PublicCtor, ServiceDescriptor<unknown>>();
}
