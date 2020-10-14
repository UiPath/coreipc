import { ICallInterceptor, ICallInterceptorContainer } from '.';

/* @internal */
export type ProxyCtor<TContract = unknown> = new (callInterceptor: ICallInterceptor<TContract>) => TContract & ICallInterceptorContainer<TContract>;
