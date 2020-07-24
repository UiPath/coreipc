import { ICallInterceptor } from '.';

/* @internal */
export const symbolOfCallInterceptor = Symbol('callInterceptor');

/* @internal */
export interface ICallInterceptorContainer<TContract = unknown> {
    [symbolOfCallInterceptor]: ICallInterceptor<TContract>;
}
