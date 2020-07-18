import { PublicCtor } from '@foundation';
import { ProxyCtor, ProxyWeaver } from '.';

/* @internal */
export class ProxyCtorMemo {
    private readonly _symbolOfProxyCtor = Symbol('proxyCtor');

    public get<TContract>(contract: PublicCtor<TContract>): ProxyCtor<TContract> {
        const container = new ProxyCtorContainer(this._symbolOfProxyCtor, contract);

        if (!container.proxyCtor) {
            container.proxyCtor = ProxyWeaver.weave(contract);
        }

        return container.proxyCtor;
    }
}

class ProxyCtorContainer<TContract> {
    constructor(
        private readonly _symbol: symbol,
        private readonly _contract: PublicCtor<TContract>,
    ) { }

    public get proxyCtor(): ProxyCtor<TContract> | undefined {
        return (this._contract as any)[this._symbol] as ProxyCtor<TContract> | undefined;
    }

    public set proxyCtor(value: ProxyCtor<TContract> | undefined) {
        (this._contract as any)[this._symbol] = value;
    }
}
