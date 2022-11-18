import { PublicCtor } from '../../..';

import {
    DispatchProxy,
    ICallInterceptor,
    ICallInterceptorContainer,
    MethodNameEnumerator,
    symbolofCallInterceptor,
} from '.';

/* @internal */
export class Weaver<TService> {
    public static weave<TService>(
        service: PublicCtor<TService>
    ): DispatchProxy<TService> {
        const instance = new Weaver<TService>(service);

        return instance.run();
    }

    constructor(private readonly _service: PublicCtor<TService>) {}

    private run(): DispatchProxy<TService> {
        // 1. Weaving a new "class"

        // 1.1. Weaving the constructor: receive the ICallInterceptor and assign it to this.<symbol>
        function EmittedClass(
            this: ICallInterceptorContainer<TService>,
            callInterceptor: ICallInterceptor<TService>
        ) {
            this[symbolofCallInterceptor] = callInterceptor;
        }

        // 1.2. Make the new class extend the contract class
        EmittedClass.constructor = this._service;
        EmittedClass.__proto__ = this._service;
        EmittedClass.prototype = new this._service();

        // 1.3. Cast the new "class" to DispatchProxyCtor<TService>
        const dispatchProxy = EmittedClass as unknown as DispatchProxy<TService>;

        // 1.4. Weave overrides for all the methods in the base class
        const methodNames = MethodNameEnumerator.enumerate(this._service);

        for (const methodName of methodNames) {
            // 1.4.i Call the ICallInterceptor feeding it all arguments and return whatever is getting returned.

            dispatchProxy.prototype[methodName] = function(this: ICallInterceptorContainer<TService>){
                const callInterceptor = this[symbolofCallInterceptor];
                const args = [...arguments];
                const result = callInterceptor.invokeMethod(methodName, args);

                return result;
            };
        }

        return dispatchProxy;
    }
}
