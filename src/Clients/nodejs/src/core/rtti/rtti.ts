// tslint:disable: only-arrow-functions
// tslint:disable: ban-types
// tslint:disable: class-name
// tslint:disable: no-namespace
// tslint:disable: no-internal-module

import { PublicCtor, Primitive } from '@foundation';
export { Primitive };

const $classInfo = Symbol();
const $classGetOrCreateMethod = Symbol();
const $hasCancellationToken = Symbol();
const $returnValueCtor = Symbol();
const $operationNameOverride = Symbol();

export function __hasCancellationToken__(target: any, propertyKey: string, descriptor: PropertyDescriptor) {
    rtti.ClassInfo.get(target.constructor)[$classGetOrCreateMethod](propertyKey)[$hasCancellationToken] = true;
}

export function __returns__(ctorOrPrimitive: PublicCtor | Primitive) {
    return function (target: any, propertyKey: string, descriptor: PropertyDescriptor) {
        rtti.ClassInfo.get(target.constructor)[$classGetOrCreateMethod](propertyKey)[$returnValueCtor] = ctorOrPrimitive;
    };
}

export function __hasName__(operationName: string) {
    return function (target: any, propertyKey: string, descriptor: PropertyDescriptor) {
        rtti.ClassInfo.get(target.constructor)[$classGetOrCreateMethod](propertyKey)[$operationNameOverride] = operationName;
    };
}

export function __endpoint__(name: string) {
    return (ctor: PublicCtor) => {
        rtti.ClassInfo.get(ctor).endpointNameOverride = name;
    };
}

/* @internal */
export module rtti {
    export class ClassInfo<TClass = any> {
        public static get<T>(ctor: PublicCtor<T>): ClassInfo<T> {
            return ctor.prototype[$classInfo] ?? (ctor.prototype[$classInfo] = new ClassInfo(ctor));
        }

        private _methods: { [name: string]: MethodInfo | undefined } = {};

        private constructor(public readonly ctor: PublicCtor<TClass>) { }

        public endpointNameOverride: string | null = null;

        public tryGetMethod(name: string): MethodInfo<TClass> | null {
            return this._methods[name] ?? null;
        }

        private [$classGetOrCreateMethod](name: string): MethodInfo<TClass> {
            return this._methods[name] ?? (this._methods[name] = new MethodInfo(this, name));
        }
    }

    export class MethodInfo<TDeclaringClass = any> {
        private [$hasCancellationToken]: boolean = false;
        private [$returnValueCtor]: PublicCtor | Primitive | null = null;
        private [$operationNameOverride]: string | null = null;

        public get hasCancellationToken(): boolean { return this[$hasCancellationToken]; }
        public get returnValueCtor(): PublicCtor | Primitive | null { return this[$returnValueCtor]; }
        public get operationNameOverride(): string | null { return this[$operationNameOverride]; }

        constructor(
            public readonly declaringClass: ClassInfo<TDeclaringClass>,
            public readonly name: string,
        ) { }
    }
}
