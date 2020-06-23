import { ICallInterceptor, ProxyBase } from '.';

/* @internal */
export type ProxyCtor<TContract> = new (callInterceptor: ICallInterceptor<TContract>) => TContract & ProxyBase<TContract>;
