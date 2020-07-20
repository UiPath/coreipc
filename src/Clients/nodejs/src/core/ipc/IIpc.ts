// tslint:disable: no-namespace no-internal-module

import { PublicCtor, Primitive, ProxyCtorMemo } from '@foundation';
import { ConfigAction, ConfigBuilder, ConfigNode } from '.';

export interface IIpc<TCallbackStore = IIpc.CallbackStore> {
    readonly contract: IIpc.ContractStore;
    readonly config: IIpc.ConfigStore;
    readonly proxy: IIpc.ProxySource;
    readonly callback: TCallbackStore;

    readonly $service: IIpc.ClassAnnotations;
    readonly $operation: IIpc.MethodAnnotations;
}

/* @internal */
export interface IIpcInternal extends IIpc<IIpc.CallbackStoreInternal> {
    readonly proxyCtorMemo: ProxyCtorMemo;
}

export module IIpc {
    export interface ConfigStore extends
        ConfigStore.Writer,
        ConfigStore.Reader { }

    export module ConfigStore {
        export interface Writer {
            write(
                action: ConfigAction<ConfigBuilder>): this;

            write<PipeName extends string = string>(
                pipeName: PipeName,
                action: ConfigAction<ConfigBuilder>): this;

            write<Service>(
                service: PublicCtor<Service>,
                action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

            write<Service, PipeName extends string = string>(
                pipeName: PipeName,
                service: PublicCtor<Service>,
                action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

            write<Service, PipeName extends string = string>(
                service: PublicCtor<Service>,
                pipeName: PipeName,
                action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;
        }

        /* @internal */
        export interface Reader {
            read<Key extends keyof ConfigNode, PipeName extends string = string, Service = unknown>(
                key: Key,
                pipeName?: PipeName,
                service?: PublicCtor<Service>): ConfigNode[Key];
        }
    }

    export interface ClassAnnotations {
        (target: PublicCtor): void;
        (args: { endpoint?: string }): (ctor: PublicCtor) => void;
    }

    export interface MethodAnnotations {
        (target: any, propertyKey: string): void;
        (args: { name?: string, returnsPromiseOf?: PublicCtor | Primitive }): (target: any, propertyKey: string) => void;
    }

    /* @internal */
    export interface ContractStore {
        getOrAdd($class: PublicCtor): ServiceInfo;
        get($class: PublicCtor): ServiceInfo | undefined;
    }

    /* @internal */
    export interface ServiceInfo {
        endpoint: string;
        readonly operations: OperationsInfo;
    }

    /* @internal */
    export interface OperationsInfo {
        get(method: string): OperationInfo | undefined;
        readonly all: Iterable<IIpc.OperationInfo>;
    }

    /* @internal */
    export interface OperationInfo {
        operationName: string;
        readonly methodName: string;
        readonly hasEndingCancellationToken: boolean;
        readonly returnType: PublicCtor;
        readonly parameterTypes: readonly PublicCtor[];
        returnsPromiseOf?: PublicCtor | Primitive;
    }

    export interface ProxySource {
        get<
            TService,
            TPipeName extends string = string>(
                pipeName: TPipeName,
                service: PublicCtor<TService>): TService;
    }

    export interface CallbackStore {
        set<TCallback>(callbackType: PublicCtor<TCallback>, pipeName: string, callback: TCallback): void;
        set<TCallback>(callbackEndpointName: string, pipeName: string, callback: TCallback): void;
    }

    /* @internal */
    export interface CallbackStoreInternal extends CallbackStore {
        get<TCallback = unknown>(callbackEndpointName: string, pipeName: string): TCallback | undefined;
    }
}
