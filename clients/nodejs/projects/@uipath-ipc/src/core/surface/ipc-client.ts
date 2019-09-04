import { ProxyFactory } from '../internals/proxy-factory';
import { Broker, IBroker } from '../internals/broker/broker';
import { PhysicalSocket } from '../../foundation/pipes/physical-socket-adapter';
import { TimeSpan } from '../../foundation/tasks/timespan';
import { ILogicalSocketFactory } from '../../foundation/pipes/logical-socket';
import { ArgumentNullError } from '../../foundation/errors/argument-null-error';
import { PublicConstructor } from '../../foundation/reflection/reflection';

export class IpcClient<TService> {
    private readonly _broker: IBroker;
    public readonly proxy: TService;

    constructor(
        public readonly pipeName: string,
        serviceCtor: PublicConstructor<TService>,
        configFunc?: (config: IpcClientConfig) => void
    ) {
        if (!pipeName) { throw new ArgumentNullError('pipeName'); }
        if (!serviceCtor) { throw new ArgumentNullError('serviceCtor'); }

        const config = new InternalIpcClientConfig();
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

export class IpcClientConfig {
    public connectTimeoutMilliseconds: number = 2000;
    public defaultCallTimeoutSeconds: number = 2;

    public callbackService?: any;
}

/* @internal */
export class InternalIpcClientConfig extends IpcClientConfig {
    public logicalSocketFactory: ILogicalSocketFactory = () => new PhysicalSocket();
    public maybeBroker: IBroker | null = null;
}
