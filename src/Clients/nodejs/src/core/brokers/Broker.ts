import { TimeSpan, ProxyCtorMemo, PublicCtor, argumentIs, ICallInterceptor } from '@foundation';
import { ConnectionHookDelegate, MethodCallHookDelegate } from '@core';
import { SchemaBuilder } from '../schema';

/* @internal */
export class Broker<TService = unknown, TCallback = unknown> {
    public static get<TService = unknown, TCallback = unknown>(settings: BrokerSettings<TService, TCallback>) {
        argumentIs(settings, 'settings', Object);

        const schema = SchemaBuilder.build(settings.service);
        throw new Error('Method not implemented.');
    }
    private static create<TService = unknown, TCallback = unknown>(settings: BrokerSettings<TService, TCallback>) {
        argumentIs(settings, 'settings', Object);

        class Interceptor implements ICallInterceptor<TService> {
            public invokeMethod(methodName: never, args: unknown[]): Promise<unknown> {
                throw new Error('Method not implemented.');
            }
        }

        const ctor = Broker._proxyCtorMemo.getOrCreate(settings.service);
        const proxy = new ctor(new Interceptor());
    }

    private static readonly _proxyCtorMemo = new ProxyCtorMemo();

    private constructor(private readonly _settings: BrokerSettings<TService, TCallback>) {
        const proxyCtor = Broker._proxyCtorMemo.getOrCreate(_settings.service);
        throw new Error('Method not implemented.');
    }

    public readonly service: TService;
}

/* @internal */
export interface BrokerSettings<TService = unknown, TCallback = unknown> {
    readonly pipeName: string;

    readonly service: PublicCtor<TService>;
    readonly callback?: TCallback;

    readonly requestTimeout: TimeSpan;
    readonly connectionHook?: ConnectionHookDelegate;
    readonly methodCallHook?: MethodCallHookDelegate;
}
