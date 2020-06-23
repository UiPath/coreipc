import { PublicCtor, Primitive } from '@foundation';

/* @internal */
export interface Schema {
    readonly endpointName: string;
    readonly className: string;
    readonly methods: { readonly [methodName: string]: MethodSchema; };
}

/* @internal */
export interface MethodSchema {
    readonly operationName: string;
    readonly methodName: string;
    readonly hasCancellationToken: boolean;
    readonly returnType: ReturnTypeSchema;
}

/* @internal */
export type ReturnTypeSchema<TReturn = void> =
    PublicCtor<TReturn> |
    Primitive |
    null;
