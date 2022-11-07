import {
    PublicCtor,
    ICallInterceptor,
    ICallInterceptorContainer,
    MethodNameEnumerator,
    ProxyCtor,
    symbolOfCallInterceptor,
} from '@foundation';

/* @internal */
export class ProxyWeaver<TContract = unknown> {
    public static weave<T = unknown>(contract: PublicCtor<T>): ProxyCtor<T> {
        const instance = new ProxyWeaver<T>(contract);
        return instance.run();
    }

    constructor(private readonly _contract: PublicCtor<TContract>) { }

    private run(): ProxyCtor<TContract> {
        function proxy(this: ICallInterceptorContainer<TContract>, callInterceptor: ICallInterceptor<TContract>) {
            this[symbolOfCallInterceptor] = callInterceptor;
        }
        proxy.constructor = this._contract;
        proxy.__proto__ = this._contract;
        proxy.prototype = new this._contract();

        const proxyCtor = proxy as unknown as ProxyCtor<TContract>;
        const methodNames = MethodNameEnumerator.enumerate(this._contract);

        for (const methodName of methodNames) {
            proxyCtor.prototype[methodName] = function (this: ICallInterceptorContainer<TContract>) {
                return this[symbolOfCallInterceptor].invokeMethod(methodName, [...arguments]);
            };
        }

        return proxyCtor;
    }
}
