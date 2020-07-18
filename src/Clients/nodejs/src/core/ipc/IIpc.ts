// tslint:disable: no-namespace no-internal-module

import { PublicCtor, Primitive } from '@foundation';
import { ConfigAction, ConfigBuilder, ConfigNode } from '.';

export interface IIpc {
    readonly contract: IIpc.ContractStore;
    readonly config: IIpc.ConfigStore;
    readonly proxy: IIpc.ProxySource;

    readonly $service: IIpc.ClassAnnotations;
    readonly $operation: IIpc.MethodAnnotations;

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
        hasEndpointName(endpointName: string): (ctor: PublicCtor) => void;
    }

    export interface MethodAnnotations {
        hasName(name: string): (target: any, propertyKey: string, descriptor: PropertyDescriptor) => void;
        returnsPromiseOf(returnType?: PublicCtor<unknown> | Primitive): (target: any, propertyKey: string, descriptor: PropertyDescriptor) => void;
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
            TPipeName extends string = string,
            TCallback = void>(
                pipeName: TPipeName,
                service: PublicCtor<TService>,
                callback?: TCallback): TService;
    }
}
