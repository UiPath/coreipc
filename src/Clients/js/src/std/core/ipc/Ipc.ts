import {
    IIpcStandard,
} from '.';

import { PipeManagerRegistry } from '../proxy-registry';
import { CallbackStore } from './callback-store/CallbackStore';
import { ContractStore } from './contract-store/ContractStore';
import { ConfigStoreWrapper } from './config-store/ConfigStoreWrapper';
import { ProxySource } from './proxy-source/ProxySource';
import { ClassAnnotationsWrapper } from './annotations/ClassAnnotationsWrapper';
import { MethodAnnotationsWrapper } from './annotations/MethodAnnotationsWrapper';
import { ProxyCtorMemo } from '@foundation';

/* @internal */
export class IpcStandard implements IIpcStandard {
    public constructor(
        contract?: IIpcStandard.ContractStore,
        config?: IIpcStandard.ConfigStoreInternal,
        proxy?: IIpcStandard.ProxySource,
        callback?: IIpcStandard.CallbackStoreInternal,
        $class?: IIpcStandard.ClassAnnotations,
        $operation?: IIpcStandard.MethodAnnotations,
    ) {
        this.contract = contract ?? new ContractStore();
        this.config = config ?? new ConfigStoreWrapper().iface;
        this.proxy = proxy ?? new ProxySource(this);
        this.callback = callback ?? new CallbackStore();

        this.$service = $class ?? new ClassAnnotationsWrapper(this).iface;
        this.$operation = $operation ?? new MethodAnnotationsWrapper(this).iface;
    }

    public readonly contract: IIpcStandard.ContractStore;
    public readonly config: IIpcStandard.ConfigStoreInternal;
    public readonly proxy: IIpcStandard.ProxySource;
    public readonly callback: IIpcStandard.CallbackStoreInternal;
    public readonly $service: IIpcStandard.ClassAnnotations;
    public readonly $operation: IIpcStandard.MethodAnnotations;

    public readonly pipeManagerRegistry = new PipeManagerRegistry(this);
    public readonly proxyCtorMemo = new ProxyCtorMemo();
}
