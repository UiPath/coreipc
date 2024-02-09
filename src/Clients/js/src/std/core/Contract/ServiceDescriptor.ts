import { OperationDescriptorTable } from '.';

/* @internal */
export type ServiceDescriptor<TService> = {
    endpoint: string;
    readonly operations: OperationDescriptorTable<TService>;
};


