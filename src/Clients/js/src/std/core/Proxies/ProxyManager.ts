import { Address } from '..';
import { ICallInterceptor } from './DispatchProxy';
import { IProxiesDomain, ProxyId } from '.';

/* @internal */
export class ProxyManager<
    TService = unknown,
    TAddress extends Address = Address
> {
    constructor(
        private readonly _domain: IProxiesDomain,
        public readonly proxyId: ProxyId<TService, TAddress>
    ) {
        this.proxy = this.createProxy();
    }

    public readonly proxy: TService;

    private createProxy(): TService {
        const _this = this;

        const EmittedInterceptorClass = class implements ICallInterceptor<TService> {
            invokeMethod(
                methodName: string & keyof TService,
                args: unknown[]
            ): Promise<unknown> {
                const result = _this._domain.channelStore.invokeMethod(
                    _this.proxyId,
                    methodName,
                    args
                );

                return result;
            }
        };

        const interceptorInstance = new EmittedInterceptorClass();

        const DispatchProxyClass = this._domain.dispatchProxyStore.get<TService>(
            this.proxyId.serviceId.service
        );

        const dispatcherProxy = new DispatchProxyClass(interceptorInstance);

        return dispatcherProxy;
    }
}
