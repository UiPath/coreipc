import { ICallInterceptor, ICallInterceptorContainer } from '.';

/* @internal */
export type DispatchProxyClass<TService = unknown> =
    new(callInterceptor: ICallInterceptor<TService>)
        => TService & ICallInterceptorContainer<TService>;
