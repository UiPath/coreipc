import { ICallInterceptor, ICallInterceptorContainer } from '.';

/* @internal */
export type DispatchProxy<TService> = new (
    callInterceptor: ICallInterceptor<TService>
) => TService & ICallInterceptorContainer<TService>;
