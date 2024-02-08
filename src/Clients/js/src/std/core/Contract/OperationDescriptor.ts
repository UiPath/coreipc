import { Primitive, PublicCtor } from '../..';

/* @internal */
export type OperationDescriptor = {
    operationName: string;

    readonly methodName: string;
    readonly hasEndingCancellationToken: boolean;
    readonly returnType: PublicCtor;
    readonly parameterTypes: readonly PublicCtor[];
    returnsPromiseOf?: PublicCtor | Primitive;
};
