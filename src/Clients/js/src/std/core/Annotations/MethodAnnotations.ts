import { Primitive, PublicCtor } from '../..';


export interface MethodAnnotations {
    (target: any, propertyKey: string): void;
    (args: { name?: string; returnsPromiseOf?: PublicCtor | Primitive; }): (
        target: any,
        propertyKey: string
    ) => void;
}
