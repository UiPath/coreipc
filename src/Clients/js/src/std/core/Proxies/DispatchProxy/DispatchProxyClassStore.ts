import { assertArgument, ConditionalWeakTable, PublicCtor } from '../../..';
import { DispatchProxyClass, Weaver } from '.';

/* @internal */
export class DispatchProxyClassStore {
    private readonly _map = new ConditionalWeakTable<PublicCtor, DispatchProxyClass>();

    public getOrCreate<TService>(service: PublicCtor<TService>): DispatchProxyClass<TService> {
        assertArgument({ service }, 'function');

        return this._map.getOrCreateValue(service, _ => Weaver.weave(service)) as any;
    }
}
