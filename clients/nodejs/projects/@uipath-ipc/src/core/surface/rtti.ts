// tslint:disable: only-arrow-functions
// tslint:disable: ban-types
// tslint:disable: class-name
// tslint:disable: no-namespace
// tslint:disable: no-internal-module

import { PublicConstructor, Maybe } from '@foundation/utils';

const $classInfo = Symbol();
const $classGetOrCreateMethod = Symbol();
const $hasCancellationToken = Symbol();
const $maybeReturnValueCtor = Symbol();

export function __hasCancellationToken__(target: any, propertyKey: string, descriptor: PropertyDescriptor) {
    const ctor: PublicConstructor<unknown> = target.constructor;
    rtti.ClassInfo.get(ctor)[$classGetOrCreateMethod](propertyKey)[$hasCancellationToken] = true;
}
export function __returns__(returnValueCtor: PublicConstructor<unknown>) {
    return function (target: any, propertyKey: string, descriptor: PropertyDescriptor) {
        const ctor: PublicConstructor<unknown> = target.constructor;
        rtti.ClassInfo.get(ctor)[$classGetOrCreateMethod](propertyKey)[$maybeReturnValueCtor] = returnValueCtor;
    };
}

/* @internal */
export module rtti {
    export class ClassInfo<T> {
        public static get<T>(ctor: PublicConstructor<T>): ClassInfo<T> {
            const prototype = ctor.prototype;
            return prototype[$classInfo] || (prototype[$classInfo] = new ClassInfo(ctor, prototype));
        }
        private _methods: { [name: string]: MethodInfo<any> | undefined } = {};
        private constructor(
            public readonly constructor: PublicConstructor<T>,
            public readonly prototype: any
        ) { }
        public tryGetMethod(name: string): Maybe<MethodInfo<T>> { return this._methods[name] || null; }
        private [$classGetOrCreateMethod](name: string): MethodInfo<T> { return this._methods[name] || (this._methods[name] = new MethodInfo(this, name)); }
    }

    export class MethodInfo<T> {
        private [$hasCancellationToken]: boolean = false;
        private [$maybeReturnValueCtor]: Maybe<PublicConstructor<unknown>>;

        public get hasCancellationToken(): boolean { return this[$hasCancellationToken]; }
        public get maybeReturnValueCtor(): Maybe<PublicConstructor<unknown>> { return this[$maybeReturnValueCtor] || null; }

        constructor(
            public readonly declaringClass: ClassInfo<T>,
            public readonly name: string
        ) { }
    }
}
