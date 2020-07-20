import { PublicCtor, TimeSpan, Timeout, CancellationToken, defaultConnectHelper } from '@foundation';
import { IIpcInternal, Message } from '../ipc';
import { IRpcChannel, Converter, IRpcChannelFactory, IMessageStreamFactory, defaultRpcChannelFactory, RpcCallContext, RpcMessage, RpcError } from '../protocol';
import { ProxyManagerRegistry } from './ProxyManagerRegistry';
import { Observer } from 'rxjs';

/* @internal */
export class PipeManager {
    constructor(
        private readonly _owner: IIpcInternal,
        public readonly pipeName: string,
        rpcChannelFactory?: IRpcChannelFactory,
        private readonly _messageStreamFactory?: IMessageStreamFactory,
    ) {
        this._rpcChannelFactory = rpcChannelFactory ?? defaultRpcChannelFactory;
    }

    public readonly proxyManagerRegistry = new ProxyManagerRegistry(this._owner, this);

    public async invokeMethod<TService = unknown>(
        service: PublicCtor<TService>,
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
            message?.requestTimeout ??
            this._owner.config.read('requestTimeout', this.pipeName, service) ??
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

        const channel = await this.ensureConnection(timeout, ct);
        const rpcResponse = await channel.call(rpcRequest, CancellationToken.none);

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
            const connectHelper = this._owner.config.read('connectHelper', this.pipeName) ?? defaultConnectHelper;

            this._channel = this._rpcChannelFactory.create(
                this.pipeName,
                connectHelper,
                timeout,
                ct,
                this._incommingCallObserver,
                this._messageStreamFactory,
            );
            const x = this._channel;
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
        const callback = this._owner.callback.get(request.Endpoint, this.pipeName) as any;

        if (!callback) {
            throw new Error(`A callback is not defined for endpoint "${request.Endpoint}".`);
        }

        const method = callback[request.MethodName] as (...args: any[]) => Promise<unknown>;
        if (!(typeof method === 'function')) {
            throw new Error(`The callback defined for endpoint "${request.Endpoint}" does not expose a method named "${request.MethodName}".`);
        }

        const args = request.Parameters.map(jsonArg => JSON.parse(jsonArg));
        let data: string | null = null;
        let error: RpcError | null = null;

        try {
            const result = await method.apply(callback, args);
            data = JSON.stringify(result);
        } catch (err) {
            error = Converter.toRpcError(err);
        }

        return new RpcMessage.Response(request.Id, data, error);
    }
}
