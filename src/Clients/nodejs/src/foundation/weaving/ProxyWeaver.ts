import { PublicCtor } from '@foundation';
import { ICallInterceptor, ProxyBase, MethodNameEnumerator, ProxyCtor, symbolOfCallInterceptor } from '.';

/* @internal */
export class ProxyWeaver<TContract = unknown> {
    public static weave<T>(contract: PublicCtor<T>): ProxyCtor<T> {
        const instance = new ProxyWeaver<T>(contract);
        return instance.run();
    }

    constructor(private readonly _contract: PublicCtor<TContract>) { }

    private run(): ProxyCtor<TContract> {
        class Proxy extends ProxyBase<TContract>  {
            constructor(callInterceptor: ICallInterceptor<TContract>) { super(callInterceptor); }
        }
        const proxyCtor = Proxy as ProxyCtor<TContract>;
        const methodNames = MethodNameEnumerator.enumerate(this._contract);

        for (const methodName of methodNames) {
            proxyCtor.prototype[methodName] = function (this: Proxy) {
                return this[symbolOfCallInterceptor].invokeMethod(methodName, [...arguments]);
            };
        }

        return Proxy as ProxyCtor<TContract>;
    }
}
