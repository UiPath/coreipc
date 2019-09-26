import { ProxyFactory } from '../internals/proxy-factory';
import { Broker, IBroker } from '../internals/broker/broker';
import { PhysicalSocket } from '../../foundation/pipes/physical-socket-adapter';
import { TimeSpan } from '../../foundation/tasks/timespan';
import { ILogicalSocketFactory } from '../../foundation/pipes/logical-socket';
import { ArgumentNullError } from '../../foundation/errors/argument-null-error';
import { PublicConstructor } from '../../foundation/reflection/reflection';
import { CancellationToken, PipeClientStream } from '../..';
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
            TimeSpan.fromSeconds(config.defaultCallTimeoutSeconds),
            TimeSpan.fromSeconds(config.defaultCallTimeoutSeconds),
            config.callbackService,
            config.maybeConnectionFactory,
            config.maybeBeforeCall,
            config.traceNetwork
        );

        this.proxy = ProxyFactory.create(serviceCtor, this._broker);
    }

    public async closeAsync(): Promise<void> {
        await this._broker.disposeAsync();
    }
}

export type ConnectionFactoryDelegate = (connect: () => Promise<PipeClientStream>, cancellationToken: CancellationToken) => Promise<PipeClientStream | void>;

export type BeforeCallDelegate = (methodName: string, newConnection: boolean, cancellationToken: CancellationToken) => Promise<void>;

export class IpcClientConfig<TService> {
    public defaultCallTimeoutSeconds: number = 40;

    public callbackService?: any;
    public traceNetwork = false;

    protected _maybeConnectionFactory: Maybe<ConnectionFactoryDelegate> = null;
    protected _maybeBeforeCall: Maybe<BeforeCallDelegate> = null;

    public setConnectionFactory(delegate: (connect: () => Promise<PipeClientStream>, cancellationToken: CancellationToken) => Promise<PipeClientStream | void>): this {
        this._maybeConnectionFactory = delegate;
        return this;
    }
    public setBeforeCall(delegate: (methodName: string, newConnection: boolean, cancellationToken: CancellationToken) => Promise<void>): this {
        this._maybeBeforeCall = delegate;
        return this;
    }
}

/* @internal */
export class InternalIpcClientConfig<TService> extends IpcClientConfig<TService> {
    public logicalSocketFactory: ILogicalSocketFactory = () => new PhysicalSocket();
    public maybeBroker: IBroker | null = null;

    public get maybeConnectionFactory(): Maybe<ConnectionFactoryDelegate> { return this._maybeConnectionFactory; }
    public get maybeBeforeCall(): Maybe<BeforeCallDelegate> { return this._maybeBeforeCall; }
}
