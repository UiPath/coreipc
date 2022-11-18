import { assertArgument, PublicCtor } from '../../..';
import { IProxiesDomain } from '..';
import { DispatchProxy, Accessor, Weaver } from '.';

/* @internal */
export class DispatchProxyStore {
    constructor(private readonly _domain: IProxiesDomain) {}

    private readonly _symbolofWeavedProxy = Symbol('WeavedProxy');

    public get<TService>(
        service: PublicCtor<TService>
    ): DispatchProxy<TService> {
        assertArgument({ service }, 'function');

        const accessor = new Accessor<
            PublicCtor<TService>,
            symbol,
            DispatchProxy<TService>
        >(service, this._symbolofWeavedProxy);

        if (!accessor.value) {
            accessor.value = Weaver.weave(service);
        }

        return accessor.value;
    }
}
