import { ICallInterceptor, ICallInterceptorContainer } from '@foundation';

/* @internal */
export type ProxyCtor<TContract = unknown> = new (callInterceptor: ICallInterceptor<TContract>) => TContract & ICallInterceptorContainer<TContract>;
