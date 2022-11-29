import { Primitive, PublicCtor } from '../..';

export interface OperationAnnotations {
    (target: any, propertyKey: string): void;
    (args: { name?: string; returnsPromiseOf?: PublicCtor | Primitive }): (
        target: any,
        propertyKey: string,
    ) => void;
}
