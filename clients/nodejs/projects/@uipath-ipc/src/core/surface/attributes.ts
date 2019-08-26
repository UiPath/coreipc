// tslint:disable: only-arrow-functions
// tslint:disable: ban-types
// tslint:disable: class-name
// tslint:disable: no-namespace
// tslint:disable: no-internal-module

const $classInfo = Symbol();
const $classGetOrCreateMethod = Symbol();
const $hasCancellationToken = Symbol();
const $maybeCtor = Symbol();

export function __hasCancellationToken__(target: any, propertyKey: string, descriptor: PropertyDescriptor) {
    rtti.ClassInfo.get(target)[$classGetOrCreateMethod](propertyKey)[$hasCancellationToken] = true;
}
export function __returns__(ctor: Function) {
    return function(target: any, propertyKey: string, descriptor: PropertyDescriptor) {
        rtti.ClassInfo.get(target)[$classGetOrCreateMethod](propertyKey)[$maybeCtor] = ctor;
    };
}

/* @internal */
export module rtti {
    export class ClassInfo {
        public static get(prototype: any): ClassInfo { return prototype[$classInfo] || (prototype[$classInfo] = new ClassInfo(prototype)); }
        private _methods: { [name: string]: MethodInfo | undefined } = {};
        private constructor(public readonly prototype: any) { }
        public tryGetMethod(name: string): MethodInfo | null { return this._methods[name] || null; }
        private [$classGetOrCreateMethod](name: string): MethodInfo { return this._methods[name] || (this._methods[name] = new MethodInfo(this, name)); }
    }

    export class MethodInfo {
        private [$hasCancellationToken]: boolean = false;
        private [$maybeCtor]: Function | null = null;

        public get hasCancellationToken(): boolean { return this[$hasCancellationToken]; }
        public get maybeCtor(): Function | null { return this[$maybeCtor]; }

        constructor(
            public readonly declaringClass: ClassInfo,
            public readonly name: string
        ) { }
    }
}
