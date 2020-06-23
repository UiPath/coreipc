import { ICallInterceptor } from '.';

/* @internal */
export const symbolOfCallInterceptor = Symbol('callInterceptor');

/* @internal */
export abstract class ProxyBase<TContract = unknown> {
    protected readonly [symbolOfCallInterceptor]: ICallInterceptor<TContract>;

    protected constructor(callInterceptor: ICallInterceptor<TContract>) {
        this[symbolOfCallInterceptor] = callInterceptor;
    }
}
