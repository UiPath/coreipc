// tslint:disable: no-namespace no-internal-module

import { PublicCtor, Primitive, ProxyCtorMemo, IAddress } from '@foundation';
import { ConfigAction, ConfigBuilder, ConfigNode } from '.';

export interface IIpcStandard<TCallbackStore = IIpcStandard.CallbackStore, TConfigStore = IIpcStandard.ConfigStore.Writer> {
    readonly config: TConfigStore;
    readonly proxy: IIpcStandard.ProxySource;
    readonly callback: TCallbackStore;

    readonly $service: IIpcStandard.ClassAnnotations;
    readonly $operation: IIpcStandard.MethodAnnotations;
}

/* @internal */
export interface IIpcInternal extends IIpcStandard<IIpcStandard.CallbackStoreInternal, IIpcStandard.ConfigStoreInternal> {
    readonly contract: IIpcStandard.ContractStore;
    readonly proxyCtorMemo: ProxyCtorMemo;
}

export module IIpcStandard {
    /* @internal */
    export interface ConfigStoreInternal extends
        ConfigStore.Writer,
        ConfigStore.Reader { }

    export module ConfigStore {
        export interface Writer {
            (action: ConfigAction<ConfigBuilder>): this;

            <Address extends IAddress = IAddress>
                (address: Address, action: ConfigAction<ConfigBuilder>): this;

            <Service>
                (service: PublicCtor<Service>, action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

            <Service, Address extends IAddress = IAddress>
                (address: Address, service: PublicCtor<Service>, action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

            <Service, Address extends IAddress = IAddress>
                (service: PublicCtor<Service>, address: Address, action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;
        }

        /* @internal */
        export interface Reader {
            read<Key extends keyof ConfigNode, Address extends IAddress = IAddress, Service = unknown>(
                key: Key,
                address?: Address,
                service?: PublicCtor<Service>): ConfigNode[Key];
        }
    }

    export interface ClassAnnotations {
        (target: PublicCtor): any;
        (args: { endpoint?: string }): any;
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
        readonly all: Iterable<IIpcStandard.OperationInfo>;
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
            TAddress extends IAddress = IAddress>(
                address: TAddress,
                service: PublicCtor<TService>): TService;
    }

    export interface CallbackStore {
        set<TCallback>(callbackType: PublicCtor<TCallback>, address: IAddress, callback: TCallback): void;
        set<TCallback>(callbackEndpointName: string, address: IAddress, callback: TCallback): void;
    }

    /* @internal */
    export interface CallbackStoreInternal extends CallbackStore {
        get<TCallback = unknown>(callbackEndpointName: string, address: IAddress): TCallback | undefined;
    }
}
