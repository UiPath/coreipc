import {
    PublicCtor,
    TimeSpan,
    Timeout,
    ArgumentOutOfRangeError,
    argumentIs,
} from '@foundation';

import {
    ConnectionHookDelegate,
    MethodCallHookDelegate,
    BrokerSettings,
} from '@core';

export class NamedPipeClientBuilder<TService = unknown, TCallback = void> {
    private readonly _settings: ClientSettings<TService, TCallback>;

    constructor(pipeName: string, serviceCtor: PublicCtor<TService>) {
        argumentIs(pipeName, 'pipeName', 'string');
        argumentIs(serviceCtor, 'serviceCtor', Function);

        this._settings = new ClientSettings(pipeName, serviceCtor);
    }

    public withRequestTimeout(requestTimeout: number): NamedPipeClientBuilder<TService, TCallback>;
    public withRequestTimeout(requestTimeout: TimeSpan): NamedPipeClientBuilder<TService, TCallback>;
    public withRequestTimeout(requestTimeout: TimeSpan | number): NamedPipeClientBuilder<TService, TCallback> {
        argumentIs(requestTimeout, 'requestTimeout', 'number', TimeSpan);
        if (typeof requestTimeout === 'number') { requestTimeout = TimeSpan.fromMilliseconds(requestTimeout); }
        if (requestTimeout.isNegative && !requestTimeout.isInfinite) {
            throw new ArgumentOutOfRangeError('requestTimeout', 'The requestTimeout must be a non-negative TimeSpan or Timeout.infiniteTimeSpan.');
        }

        this._settings.requestTimeout = requestTimeout;
        return this;
    }

    public withConnectionHook(connectionHook: ConnectionHookDelegate): NamedPipeClientBuilder<TService, TCallback> {
        argumentIs(connectionHook, 'connectionHook', Function);

        this._settings.connectionHook = connectionHook;
        return this;
    }

    public withMethodCallHook(methodCallHook: MethodCallHookDelegate): NamedPipeClientBuilder<TService, TCallback> {
        argumentIs(methodCallHook, 'methodCallHook', Function);

        this._settings.methodCallHook = methodCallHook;
        return this;
    }

    public withCallback<TCallback2>(callback: TCallback2): NamedPipeClientBuilder<TService, TCallback2> {
        argumentIs(callback, 'callback', Object);

        const me = this as any as NamedPipeClientBuilder<TService, TCallback2>;
        me._settings.callback = callback;
        return me;
    }

    public build(): TService {
        const foo = 'bar';
        return null as any;
    }
}

/* @internal */
export class ClientSettings<TService = unknown, TCallback = void> implements BrokerSettings<TService, TCallback> {
    constructor(
        public readonly pipeName: string,
        public readonly service: PublicCtor<TService>,
    ) { }

    public requestTimeout = Timeout.infiniteTimeSpan;
    public connectionHook?: ConnectionHookDelegate;
    public methodCallHook?: MethodCallHookDelegate;
    public callback?: TCallback;
}
