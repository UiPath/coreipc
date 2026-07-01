import { PublicCtor } from '../..';
import { ServiceDescriptor } from './ServiceDescriptor';

/* @internal */
export interface IContractStore {
    getOrCreate<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService>;

    maybeGet<TService = unknown>($class: PublicCtor<TService>): ServiceDescriptor<TService> | undefined;

    /**
     * Looks up a registered contract by its endpoint name (defaults to the
     * contract class name). Used on the callee side, where an incoming call
     * identifies its contract by endpoint name rather than by class.
     */
    maybeGetByEndpoint(endpoint: string): ServiceDescriptor<unknown> | undefined;
}
