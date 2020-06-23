import { PublicCtor, argumentIs, argumentIsNonEmptyString } from '@foundation';
import { rtti } from '@core-rtti';
import { MethodSchema } from '.';

export type MemberName<T> = string & keyof T;
type MethodSchemaLookup<T> = (methodName: MemberName<T>) => MethodSchema;
type MethodSchemaOrLookup<T> = MethodSchema | MethodSchemaLookup<T>;

/* @internal */
export class MethodSchemaBuilder {
    public static build<T = unknown>(contract: PublicCtor<T>): MethodSchemaLookup<T>;
    public static build<T = unknown>(contract: PublicCtor<T>, methodName: MemberName<T>): MethodSchema;
    public static build<T = unknown>(contract: PublicCtor<T>, methodName?: MemberName<T>): MethodSchemaOrLookup<T> {
        argumentIs(contract, 'contract', 'function');
        const classInfo = rtti.ClassInfo.get(contract);

        return methodName === undefined
            ? MethodSchemaBuilder._bind(classInfo)
            : MethodSchemaBuilder._exec(classInfo, methodName)
            ;
    }

    private static _bind<T>(declaringClass: rtti.ClassInfo<T>): MethodSchemaLookup<T> {
        return (methodName: MemberName<T>) => MethodSchemaBuilder._exec(declaringClass, methodName);
    }

    private static _exec<T>(declaringClass: rtti.ClassInfo<T>, methodName: MemberName<T>): MethodSchema {
        argumentIsNonEmptyString(methodName, 'methodName');

        const maybeMethodInfo = declaringClass.tryGetMethod(methodName);
        return {
            hasCancellationToken: maybeMethodInfo?.hasCancellationToken ?? false,
            methodName,
            operationName: maybeMethodInfo?.operationNameOverride ?? methodName,
            returnType: maybeMethodInfo?.returnValueCtor ?? null,
        };
    }
}
