import { ICallInterceptor } from '.';

/* @internal */
export const symbolofCallInterceptor = Symbol('CallInterceptor');

/* @internal */
export interface ICallInterceptorContainer<TService = unknown> {
    [symbolofCallInterceptor]: ICallInterceptor<TService>;
}
