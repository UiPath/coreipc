import { ConditionalWeakTable, Dictionary, PublicCtor } from '../../bcl';
import { Address, AddressBuilder } from '..';
import { ICallInterceptor, IServiceProvider, ProxyId } from '.';

/* @internal */
export class ProxySource<TAddressBuilder extends AddressBuilder> {
    private static readonly _serviceToAddressToProxy = new ConditionalWeakTable<PublicCtor, Dictionary<string, unknown>>();

    constructor(
        private readonly _sp: IServiceProvider<TAddressBuilder>,
    ) { }

    public resolve<TService, TAddress extends Address = Address>(proxyId: ProxyId<TService, TAddress>): TService {
        return ProxySource
            ._serviceToAddressToProxy
            .getOrCreateValue(proxyId.service, Dictionary.create)
            .getOrCreateValue(proxyId.address.key, _ => this.createProxy(proxyId)) as TService
            ;
    }

    private createProxy<TService, TAddress extends Address>(proxyId: ProxyId<TService, TAddress>): TService {
        const _this = this;

        // The following inline class captures the proxyId parameter.
        const InterceptorClass = class implements ICallInterceptor<TService>
        {
            invokeMethod(methodName: keyof TService & string, args: unknown[]): Promise<unknown> {
                return _this._sp.wire.invokeMethod(
                    proxyId,
                    methodName,
                    args,
                );
            }
        };

        const DispatcherProxyClass = this._sp.dispatchProxies.getOrCreate(proxyId.service);

        const proxy = new DispatcherProxyClass(new InterceptorClass());

        return proxy;
    }
}

