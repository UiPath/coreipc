import { ProxyFactory } from '@core/internals/proxy-factory';
import { Broker, IBroker, IBrokerCtorParams } from '@core/internals/broker';
import { ArgumentNullError } from '@foundation/errors';
import { PublicConstructor, Maybe } from '@foundation/utils';
import { ILogicalSocketFactory, PhysicalSocket, IPipeClientStream } from '@foundation/pipes';
import { CancellationToken, TimeSpan } from '@foundation/threading';

export type ConnectionFactoryDelegate = (connect: () => Promise<IPipeClientStream>, cancellationToken: CancellationToken) => Promise<IPipeClientStream | void>;
export type BeforeCallDelegate = (methodName: string, newConnection: boolean, cancellationToken: CancellationToken) => Promise<void>;

export class IpcClient<TService> {
    private readonly _broker: IBroker;
    public readonly proxy: TService;

    constructor(
        public readonly pipeName: string,
        serviceCtor: PublicConstructor<TService>,
        configFunc?: (config: IIpcClientConfig) => void
    ) {
        if (!pipeName) { throw new ArgumentNullError('pipeName'); }
        if (!serviceCtor) { throw new ArgumentNullError('serviceCtor'); }

        const config = new IpcClientConfig();
        if (configFunc) { configFunc(config); }

        this._broker = config.maybeBroker || new Broker(IpcClient.buildBrokerCtorParams(pipeName, config));
        this.proxy = ProxyFactory.create(serviceCtor, this._broker);
    }

    public async closeAsync(): Promise<void> {
        await this._broker.disposeAsync();
    }

    private static buildBrokerCtorParams(pipeName: string, config: IpcClientConfig): IBrokerCtorParams {
        return {
            factory: config.logicalSocketFactory,
            pipeName,
            connectTimeout: TimeSpan.fromSeconds(config.defaultCallTimeoutSeconds),
            defaultCallTimeout: TimeSpan.fromSeconds(config.defaultCallTimeoutSeconds),
            callback: config.callbackService,
            connectionFactory: config.maybeConnectionFactory,
            beforeCall: config.maybeBeforeCall,
            traceNetwork: config.traceNetwork
        };
    }
}

export interface IIpcClientConfig {
    defaultCallTimeoutSeconds: number;

    callbackService?: any;
    traceNetwork: boolean;

    setConnectionFactory(
        delegate: (connect: () => Promise<IPipeClientStream>, cancellationToken: CancellationToken) => Promise<IPipeClientStream | void>
    ): this;

    setBeforeCall(
        delegate: (methodName: string, newConnection: boolean, cancellationToken: CancellationToken) => Promise<void>
    ): this;
}

/* @internal */
export class IpcClientConfig implements IIpcClientConfig {
    // #region " Implementation of IIpcClientConfig "
    public defaultCallTimeoutSeconds: number = 40;

    public callbackService?: any;
    public traceNetwork = false;

    protected _maybeConnectionFactory: Maybe<ConnectionFactoryDelegate> = null;
    protected _maybeBeforeCall: Maybe<BeforeCallDelegate> = null;

    public setConnectionFactory(delegate: (connect: () => Promise<IPipeClientStream>, cancellationToken: CancellationToken) => Promise<IPipeClientStream | void>): this {
        if (!delegate) { throw new ArgumentNullError('delegate'); }

        this._maybeConnectionFactory = delegate;
        return this;
    }
    public setBeforeCall(delegate: (methodName: string, newConnection: boolean, cancellationToken: CancellationToken) => Promise<void>): this {
        if (!delegate) { throw new ArgumentNullError('delegate'); }

        this._maybeBeforeCall = delegate;
        return this;
    }
    // #endregion

    /* istanbul ignore next */
    public logicalSocketFactory: ILogicalSocketFactory = () => new PhysicalSocket();
    public maybeBroker: IBroker | null = null;

    public get maybeConnectionFactory(): Maybe<ConnectionFactoryDelegate> { return this._maybeConnectionFactory; }
    public get maybeBeforeCall(): Maybe<BeforeCallDelegate> { return this._maybeBeforeCall; }
}
