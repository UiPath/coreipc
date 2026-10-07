import { PublicCtor } from '../..';
import { ServiceDescriptor } from './ServiceDescriptor';

/* @internal */
export interface IContractStore {
    getOrCreate<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService>;

    maybeGet<TService = unknown>($class: PublicCtor<TService>): ServiceDescriptor<TService> | undefined;

    /** For the callee side, where an incoming call identifies its contract by name. */
    maybeGetByEndpoint(endpoint: string): ServiceDescriptor<unknown> | undefined;
}
