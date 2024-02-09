import { OperationDescriptor } from '.';

/* @internal */
export type OperationDescriptorTable<TService> = {
    getOrCreate(method: keyof TService & string): OperationDescriptor;
    maybeGet(method: keyof TService & string): OperationDescriptor | undefined;

    readonly all: Iterable<OperationDescriptor>;
};
