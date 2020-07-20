import {
    IIpc,
    ClassAnnotationsWrapper,
    MethodAnnotationsWrapper,
    ConfigStore,
    ProxySource,
    ContractStore,
} from '.';

import {
    PipeManagerRegistry,
} from '../proxy-registry/PipeManagerRegistry';

import { ProxyCtorMemo } from '@foundation';
import { CallbackStore } from './callback-store/CallbackStore';

/* @internal */
export class Ipc implements IIpc {
    public constructor(
        contract?: IIpc.ContractStore,
        config?: IIpc.ConfigStore,
        proxy?: IIpc.ProxySource,
        callback?: IIpc.CallbackStoreInternal,
        $class?: IIpc.ClassAnnotations,
        $operation?: IIpc.MethodAnnotations,
    ) {
        this.contract = contract ?? new ContractStore();
        this.config = config ?? new ConfigStore();
        this.proxy = proxy ?? new ProxySource(this);
        this.callback = callback ?? new CallbackStore();

        this.$service = $class ?? new ClassAnnotationsWrapper(this).iface;
        this.$operation = $operation ?? new MethodAnnotationsWrapper(this).iface;
    }

    public readonly contract: IIpc.ContractStore;
    public readonly config: IIpc.ConfigStore;
    public readonly proxy: IIpc.ProxySource;
    public readonly callback: IIpc.CallbackStoreInternal;
    public readonly $service: IIpc.ClassAnnotations;
    public readonly $operation: IIpc.MethodAnnotations;

    public readonly pipeManagerRegistry = new PipeManagerRegistry(this);
    public readonly proxyCtorMemo = new ProxyCtorMemo();
}
