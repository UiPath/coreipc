import { Observer } from 'rxjs';

import {
    PublicCtor,
    NamedPublicCtor,
    TimeSpan,
    Timeout,
    CancellationToken,
    defaultConnectHelper,
    ICallInterceptor,
    Trace,
    IAddress,
} from '@foundation';

import {
    IRpcChannel,
    Converter,
    IRpcChannelFactory,
    IMessageStreamFactory,
    defaultRpcChannelFactory,
    RpcCallContext,
    RpcMessage,
    IpcError,
} from '../protocol';

import {
    IIpcInternal,
    Message,
} from '../ipc';

/* @internal */
export class PipeManager {
    constructor(
        private readonly _owner: IIpcInternal,
        public readonly address: IAddress,
        rpcChannelFactory?: IRpcChannelFactory,
        private readonly _messageStreamFactory?: IMessageStreamFactory,
    ) {
        this._rpcChannelFactory = rpcChannelFactory ?? defaultRpcChannelFactory;
    }

    public readonly proxyManagerRegistry = new ProxyManagerRegistry(this._owner, this);

    public async invokeMethod<TService = unknown>(
        service: NamedPublicCtor<TService>,
        methodName: string & keyof TService,
        args: unknown[],
    ): Promise<unknown> {
        const serviceContract = this._owner.contract.get(service);
        const operationContract = serviceContract?.operations.get(methodName);

        const endpoint = serviceContract?.endpoint ?? service.name;
        const operationName = operationContract?.operationName ?? methodName;
        const hasEndingCt = operationContract?.hasEndingCancellationToken ?? false;
        const returnsPromiseOf = operationContract?.returnsPromiseOf;

        let message: Message | undefined;
        let ct: CancellationToken | undefined;

        for (const arg of args) {
            if (arg instanceof Message) { message = arg; }
            if (arg instanceof CancellationToken) { ct = arg; }
        }

        ct = ct ?? CancellationToken.none;

        const timeout =
            message?.RequestTimeout ??
            this._owner.config.read('requestTimeout', this.address, service) ??
            Timeout.infiniteTimeSpan;

        if (hasEndingCt && (args.length === 0 || !(args[args.length - 1] instanceof CancellationToken))) {
            args = [...args, CancellationToken.none];
        }

        const rpcRequest = Converter.toRpcRequest(
            endpoint,
            operationName,
            args,
            timeout,
        );

        const channel = await this.ensureConnection(Timeout.infiniteTimeSpan, ct);

        const rpcResponse = await channel.call(rpcRequest, timeout, ct);

        const result = Converter.fromRpcResponse(rpcResponse, rpcRequest) as any;
        if (returnsPromiseOf instanceof Function) {
            result.constructor = returnsPromiseOf;
            result.__proto__ = returnsPromiseOf.prototype;
        }
        return result;
    }

    private readonly _rpcChannelFactory: IRpcChannelFactory;
    private _channel: IRpcChannel | undefined;

    private async ensureConnection(timeout: TimeSpan, ct: CancellationToken): Promise<IRpcChannel> {
        if (!this._channel || this._channel.isDisposed) {
            const connectHelper = this._owner.config.read('connectHelper', this.address) ?? defaultConnectHelper;

            this._channel = this._rpcChannelFactory.create(
                this.address,
                connectHelper,
                timeout,
                ct,
                this._incommingCallObserver,
                this._messageStreamFactory,
            );
        }

        return this._channel;
    }

    private _incommingCallObserver = new (class implements Observer<RpcCallContext.Incomming> {
        constructor(private readonly _owner: PipeManager) { }

        public closed?: boolean;
        public async next(value: RpcCallContext.Incomming): Promise<void> {
            value.respond(await this._owner.invokeCallback(value.request));
        }
        public error(err: any): void { }
        public complete(): void { }
    })(this);

    private async invokeCallback(request: RpcMessage.Request): Promise<RpcMessage.Response> {
        const callback = this._owner.callback.get(request.Endpoint, this.address) as any;

        if (!callback) {
            throw new Error(`A callback is not defined for endpoint "${request.Endpoint}".`);
        }

        const method = callback[request.MethodName] as (...args: any[]) => Promise<unknown>;
        if (!(typeof method === 'function')) {
            throw new Error(`The callback defined for endpoint "${request.Endpoint}" does not expose a method named "${request.MethodName}".`);
        }

        const args = request.Parameters.map(jsonArg => JSON.parse(jsonArg));
        let data: string | null = null;
        let error: IpcError | null = null;

        try {
            const result = await method.apply(callback, args);
            data = JSON.stringify(result);
        } catch (err) {
            error = Converter.toRpcError(err);
        }

        return new RpcMessage.Response(request.Id, data, error);
    }
}

/* @internal */
export class PipeManagerRegistry {
    constructor(
        private readonly _owner: IIpcInternal,
    ) { }

    public get(address: IAddress): PipeManager {
        return this._map.get(address.key) ?? this.add(address);
    }

    private add(address: IAddress): PipeManager {
        const pipeManager = new PipeManager(this._owner, address);
        this._map.set(address.key, pipeManager);
        return pipeManager;
    }

    private readonly _map = new Map<string, PipeManager>();
}

/* @internal */
export class ProxyManager<TService = unknown> {
    private static readonly _trace = Trace.category('ProxyManager');

    constructor(
        owner: IIpcInternal,
        pipeManager: PipeManager,
        service: NamedPublicCtor<TService>,
    ) {
        const classOfProxy = owner.proxyCtorMemo.get(service);
        const classOfcallInterceptor = class implements ICallInterceptor<TService> {
            public invokeMethod(methodName: never, args: unknown[]): Promise<unknown> {
                ProxyManager._trace.log({
                    $type: `ICallInterceptor<${service.name}>`,
                    $operation: 'invokeMethod(methodName, args)',
                    $details: {
                        address: pipeManager.address,
                        service: service.name,
                        method: methodName,
                        args,
                    },
                });
                return pipeManager.invokeMethod(service, methodName, args);
            }
        };

        const callInterceptor = new classOfcallInterceptor();
        this.proxy = new classOfProxy(callInterceptor);
    }

    public readonly proxy: TService = null as any;
}

/* @internal */
export class ProxyManagerRegistry {
    constructor(
        private readonly _owner: IIpcInternal,
        private readonly _pipeManager: PipeManager,
    ) { }

    public get<TService = unknown>(service: NamedPublicCtor<TService>): ProxyManager<TService> {
        return this._map.get(service) as ProxyManager<TService> ?? this.add(service);
    }

    private add<TService = unknown>(service: NamedPublicCtor<TService>): ProxyManager<TService> {
        const result = new ProxyManager(this._owner, this._pipeManager, service);
        this._map.set(service, result);
        return result;
    }

    private readonly _map = new Map<PublicCtor, ProxyManager>();
}
