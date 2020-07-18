import { PublicCtor, TimeSpan, Timeout, CancellationToken, defaultConnectHelper } from '@foundation';
import { Ipc, Message } from '../ipc';
import { RpcChannel, Converter } from '../protocol';
import { ProxyManagerRegistry } from './ProxyManagerRegistry';

/* @internal */
export class PipeManager {
    constructor(
        private readonly _owner: Ipc,
        public readonly pipeName: string,
    ) { }

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

    private async ensureConnection(timeout: TimeSpan, ct: CancellationToken): Promise<RpcChannel.Impl> {
        if (this._channel?.isAlive !== true) {
            await this._channel?.disposeAsync();
            const connectHelper = this._owner.config.read('connectHelper', this.pipeName) ?? defaultConnectHelper;
            this._channel = await RpcChannel.Impl.connect(this.pipeName, connectHelper, timeout, ct);
        }

        return this._channel;
    }

    private _channel: RpcChannel.Impl | undefined;
}
