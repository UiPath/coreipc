import { CancellationToken, PublicCtor, Timeout, TimeSpan } from '../..';

import {
    IServiceProvider,
    Address,
    Converter,
    IRpcChannel,
    defaultConnectHelper,
    IRpcChannelFactory,
    RpcCallContext,
    RpcMessage,
    IpcError,
    IMessageStream,
} from '..';

import { RpcRequestFactory } from '.';
import { Observer } from 'rxjs';

/* @internal */
export class ChannelManager<TAddress extends Address = Address> {
    private _latestChannel: IRpcChannel | undefined;

    constructor(
        private readonly _sp: IServiceProvider,
        private readonly _address: TAddress,
        private readonly _rpcChannelFactory: IRpcChannelFactory,
        private readonly _messageStreamFactory?: IMessageStream.Factory,
    ) {}

    async invokeMethod<TService>(service: PublicCtor<TService>, methodName: keyof TService & string, args: unknown[]): Promise<unknown> {
        const [rpcRequest, returnsPromiseOf, ct, timeout] = RpcRequestFactory.create(
        {
            sp: this._sp,
            service,
            address: this._address,
            methodName,
            args,
        });

        const channel = await this.ensureConnection(Timeout.infiniteTimeSpan, ct);

        const rpcResponse = await channel.call(rpcRequest, timeout, ct);

        const result = Converter.unwrapRpcResponse(rpcResponse, rpcRequest) as any;

        if (returnsPromiseOf instanceof Function) {
            result.constructor = returnsPromiseOf;
            result.__proto__ = returnsPromiseOf.prototype;
        }
        return result;
    }

    private async ensureConnection(timeout: TimeSpan, ct: CancellationToken): Promise<IRpcChannel> {
        if (!this._latestChannel || this._latestChannel.isDisposed) {
            const connectHelper =
                this._sp.configStore.getConnectHelper(this._address) ??
                defaultConnectHelper;

            this._latestChannel = this._rpcChannelFactory.create(
                this._address,
                connectHelper,
                timeout,
                ct,
                this._incommingCallObserver,
                this._messageStreamFactory,
            );
        }

        return this._latestChannel;
    }

    private async invokeCallback(
        request: RpcMessage.Request,
    ): Promise<RpcMessage.Response> {
        const callback = this._sp.callbackStore.get(
            request.Endpoint,
            this._address,
        ) as any;

        if (!callback) {
            throw new Error(
                `A callback is not defined for endpoint "${request.Endpoint}".`,
            );
        }

        const method = callback[request.MethodName] as (
            ...args: any[]
        ) => Promise<unknown>;
        if (!(typeof method === 'function')) {
            throw new Error(
                `The callback defined for endpoint "${request.Endpoint}" does not expose a method named "${request.MethodName}".`,
            );
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
    private readonly _incommingCallObserver = new (class
        implements Observer<RpcCallContext.Incomming>
    {
        constructor(
            private readonly _channelManager: ChannelManager<TAddress>,
        ) {}

        public closed?: boolean;
        public async next(
            incommingContext: RpcCallContext.Incomming,
        ): Promise<void> {
            incommingContext.respond(
                await this._channelManager.invokeCallback(
                    incommingContext.request,
                ),
            );
        }
        public error(err: any): void {}
        public complete(): void {}
    })(this);
}
