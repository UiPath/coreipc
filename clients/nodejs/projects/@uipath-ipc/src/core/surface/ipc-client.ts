import { ProxyFactory } from '../internals/proxy-factory';
import { Broker, IBroker } from '../internals/broker/broker';
import { PhysicalSocket } from '../../foundation/pipes/physical-socket-adapter';
import { TimeSpan } from '../../foundation/tasks/timespan';
import { ILogicalSocketFactory } from '../../foundation/pipes/logical-socket';
import { ArgumentNullError } from '../../foundation/errors/argument-null-error';
import { PublicConstructor } from '../../foundation/reflection/reflection';
import { CancellationToken } from '../..';
import { Maybe } from '../../foundation/data-structures/maybe';

export class IpcClient<TService> {
    private readonly _broker: IBroker;
    public readonly proxy: TService;

    constructor(
        public readonly pipeName: string,
        serviceCtor: PublicConstructor<TService>,
        configFunc?: (config: IpcClientConfig<TService>) => void
    ) {
        if (!pipeName) { throw new ArgumentNullError('pipeName'); }
        if (!serviceCtor) { throw new ArgumentNullError('serviceCtor'); }

        const config = new InternalIpcClientConfig<TService>();
        if (configFunc) { configFunc(config); }

        this._broker = config.maybeBroker || new Broker(
            config.logicalSocketFactory,
            pipeName,
            TimeSpan.fromMilliseconds(config.connectTimeoutMilliseconds),
            TimeSpan.fromSeconds(config.defaultCallTimeoutSeconds),
            config.callbackService);

        this.proxy = ProxyFactory.create(serviceCtor, this._broker);
    }

    public async closeAsync(): Promise<void> {
        await this._broker.disposeAsync();
    }
}

export interface CallInfo<TService> {
    readonly newConnection: boolean;
    readonly proxy: TService;
}
export type BeforeConnectDelegate = (cancellationToken: CancellationToken) => Promise<void>;
export type BeforeCallDelegate<TService> = (callInfo: CallInfo<TService>, cancellationToken: CancellationToken) => Promise<void>;

export class IpcClientConfig<TService> {
    public connectTimeoutMilliseconds: number = 2000;
    public defaultCallTimeoutSeconds: number = 2;

    public callbackService?: any;

    protected _maybeBeforeConnect: Maybe<BeforeConnectDelegate> = null;
    protected _maybeBeforeCall: Maybe<BeforeCallDelegate<TService>> = null;

    public setBeforeConnect(delegate: BeforeConnectDelegate): this {
        this._maybeBeforeConnect = delegate;
        return this;
    }
    public setBeforeCall(delegate: BeforeCallDelegate<TService>): this {
        this._maybeBeforeCall = delegate;
        return this;
    }
}

/* @internal */
export class InternalIpcClientConfig<TService> extends IpcClientConfig<TService> {
    public logicalSocketFactory: ILogicalSocketFactory = () => new PhysicalSocket();
    public maybeBroker: IBroker | null = null;

    public get maybeBeforeConnect(): Maybe<BeforeConnectDelegate> { return this._maybeBeforeConnect; }
    public get maybeBeforeCall(): Maybe<Maybe<BeforeCallDelegate<TService>>> { return this._maybeBeforeCall; }
}
