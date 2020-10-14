import * as fs from 'fs';

import {
    IIpc,
} from '.';

import { PipeManagerRegistry } from '../proxy-registry';
import { ProxyCtorMemo } from '../../foundation';
import { CallbackStore } from './callback-store/CallbackStore';
import { ContractStore } from './contract-store/ContractStore';
import { ConfigStoreWrapper } from './config-store/ConfigStoreWrapper';
import { ProxySource } from './proxy-source/ProxySource';
import { ClassAnnotationsWrapper } from './annotations/ClassAnnotationsWrapper';
import { MethodAnnotationsWrapper } from './annotations/MethodAnnotationsWrapper';
import { CoreIpcPlatform } from '../../foundation/named-pipes/CoreIpcPlatform';

/* @internal */
export class Ipc implements IIpc {
    public constructor(
        contract?: IIpc.ContractStore,
        config?: IIpc.ConfigStoreInternal,
        proxy?: IIpc.ProxySource,
        callback?: IIpc.CallbackStoreInternal,
        $class?: IIpc.ClassAnnotations,
        $operation?: IIpc.MethodAnnotations,
    ) {
        this.contract = contract ?? new ContractStore();
        this.config = config ?? new ConfigStoreWrapper().iface;
        this.proxy = proxy ?? new ProxySource(this);
        this.callback = callback ?? new CallbackStore();

        this.$service = $class ?? new ClassAnnotationsWrapper(this).iface;
        this.$operation = $operation ?? new MethodAnnotationsWrapper(this).iface;
    }

    public readonly contract: IIpc.ContractStore;
    public readonly config: IIpc.ConfigStoreInternal;
    public readonly proxy: IIpc.ProxySource;
    public readonly callback: IIpc.CallbackStoreInternal;
    public readonly $service: IIpc.ClassAnnotations;
    public readonly $operation: IIpc.MethodAnnotations;

    public readonly pipeManagerRegistry = new PipeManagerRegistry(this);
    public readonly proxyCtorMemo = new ProxyCtorMemo();

    public pipeExists(shortName: string): Promise<boolean> { return CoreIpcPlatform.current.pipeExists(shortName); }
}
