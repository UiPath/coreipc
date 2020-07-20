import { PublicCtor, ICallInterceptor } from '@foundation';
import { IIpcInternal } from '..';
import { PipeManager } from './PipeManager';

/* @internal */
export class ProxyManager<TService = unknown> {
    constructor(
        owner: IIpcInternal,
        pipeManager: PipeManager,
        service: PublicCtor<TService>,
    ) {
        const classOfProxy = owner.proxyCtorMemo.get(service);
        const classOfcallInterceptor = class implements ICallInterceptor<TService> {
            public invokeMethod(methodName: never, args: unknown[]): Promise<unknown> {
                return pipeManager.invokeMethod(service, methodName, args);
            }
        };

        const callInterceptor = new classOfcallInterceptor();
        this.proxy = new classOfProxy(callInterceptor);
    }

    public readonly proxy: TService = null as any;
}
