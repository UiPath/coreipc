import { PublicCtor } from '../..';
import { ServiceDescriptor } from './ServiceDescriptor';

/* @internal */
export interface IContractStore {
    getOrCreate<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService>;

    maybeGet<TService = unknown>($class: PublicCtor<TService>): ServiceDescriptor<TService> | undefined;
}
