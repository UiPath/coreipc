import { Address, AddressBuilder } from '..';
import { ICallInterceptor } from './DispatchProxy';
import { IServiceProvider, ProxyId } from '.';

/* @internal */
export class ProxyManager<
    TAddressBuilder extends AddressBuilder,
    TService = unknown,
    TAddress extends Address = Address,
> {
    constructor(
        private readonly _serviceProvider: IServiceProvider<TAddressBuilder>,
        public readonly proxyId: ProxyId<TService, TAddress>,
    ) {
        this.proxy = this.createProxy();
    }

    public readonly proxy: TService;

    private createProxy(): TService {
        const _this = this;

        const EmittedInterceptorClass = class
            implements ICallInterceptor<TService>
        {
            invokeMethod(
                methodName: keyof TService & string,
                args: unknown[],
            ): Promise<unknown> {
                const result = _this._serviceProvider.channelStore.invokeMethod(
                    _this.proxyId,
                    methodName,
                    args,
                );

                return result;
            }
        };

        const interceptorInstance = new EmittedInterceptorClass();

        const DispatchProxyClass =
            this._serviceProvider.dispatchProxyStore.get<TService>(
                this.proxyId.service,
            );

        const dispatcherProxy = new DispatchProxyClass(interceptorInstance);

        return dispatcherProxy;
    }
}
