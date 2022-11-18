import { Address } from '..';
import { IServiceProvider, ProxyId, ProxyManager } from '.';

/* @internal */
export class ProxyStore {
    constructor(private readonly _domain: IServiceProvider) {}

    public resolve<TService, TAddress extends Address = Address>(
        proxyId: ProxyId<TService, TAddress>,
    ): TService {
        return this.getOrCreateManager(proxyId).proxy;
    }

    private getOrCreateManager<TService, TAddress extends Address>(
        proxyId: ProxyId<TService, TAddress>,
    ): ProxyManager<TService> {
        let value = this._memo.get(proxyId.key) as ProxyManager<TService, TAddress> | undefined;

        if (value === undefined) {
            value = new ProxyManager<TService, TAddress>(this._domain, proxyId);
            this._memo.set(proxyId.key, value);
        }

        return value;
    }

    private readonly _memo = new Map<string, ProxyManager>();
}
