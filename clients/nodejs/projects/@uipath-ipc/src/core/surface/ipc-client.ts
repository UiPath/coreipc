import { ProxyFactory } from '../internals/proxy-factory';
import { Broker } from '../internals/broker/broker';

export class IpcClient<TService> {
    private readonly _broker: Broker;
    public readonly proxy: TService;

    constructor(
        public readonly pipeName: string,
        serviceSample: TService,
        configFunc?: (config: IpcClientConfig) => void
    ) {
        const config = new IpcClientConfig();
        if (configFunc) { configFunc(config); }

        this._broker = new Broker(pipeName, config.connectTimeoutMilliseconds, config.defaultCallTimeoutSeconds, config.callbackService);
        this.proxy = ProxyFactory.create(serviceSample, this._broker);
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
