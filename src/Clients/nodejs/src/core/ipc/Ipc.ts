import {
    IIpc,
    ClassAnnotationsWrapper,
    MethodAnnotations,
    ConfigStore,
    ProxySource,
    ContractStore,
} from '.';

import {
    PipeManagerRegistry,
} from '../proxy-registry/PipeManagerRegistry';
import { ProxyCtorMemo } from '@foundation';

/* @internal */
export class Ipc implements IIpc {
    public constructor(
        contract?: IIpc.ContractStore,
        config?: IIpc.ConfigStore,
        proxy?: IIpc.ProxySource,
        $class?: IIpc.ClassAnnotations,
        $operation?: IIpc.MethodAnnotations,
    ) {
        this.contract = contract ?? new ContractStore();
        this.config = config ?? new ConfigStore();
        this.proxy = proxy ?? new ProxySource(this);
        this.$service = $class ?? new ClassAnnotationsWrapper(this).contract;
        this.$operation = $operation ?? new MethodAnnotations(this);
    }

    public readonly contract: IIpc.ContractStore;
    public readonly config: IIpc.ConfigStore;
    public readonly proxy: IIpc.ProxySource;
    public readonly $service: IIpc.ClassAnnotations;
    public readonly $operation: IIpc.MethodAnnotations;

    public readonly pipeManagerRegistry = new PipeManagerRegistry(this);
    public readonly proxyCtorMemo = new ProxyCtorMemo();
}
