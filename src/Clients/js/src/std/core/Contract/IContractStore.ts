import { Primitive, PublicCtor } from '../..';

/* @internal */
export interface IContractStore {
    getOrCreate<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService>;

    maybeGet<TService>($class: PublicCtor<TService>): ServiceDescriptor<TService> | undefined;
}

/* @internal */
export type ServiceDescriptor<TService> = {
    readonly endpoint: string;
    readonly operations: OperationDescriptorTable;
};

/* @internal */
export type OperationDescriptorTable = {
    maybeGet(method: string): OperationDescriptor | undefined;

    readonly all: Iterable<OperationDescriptor>;
};

/* @internal */
export type OperationDescriptor = {
    readonly operationName: string;

    readonly methodName: string;
    readonly hasEndingCancellationToken: boolean;
    readonly returnType: PublicCtor;
    readonly parameterTypes: readonly PublicCtor[];
    readonly returnsPromiseOf?: PublicCtor | Primitive;
};
