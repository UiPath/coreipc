import { _jsargs } from '../..';
import {
    ArgumentOutOfRangeError,
    nameof,
    PublicCtor,
} from '../../../../src/std';
import { MethodArgs, MethodNames } from './funky';

export type CoverTypeContext<
    T extends abstract new (...args: any) => any,
    TStatic extends abstract new (...args: any) => T,
> = {} & CoverInstanceMethods<T> & CoverStaticMethods<TStatic> & CoverCtors<T>;

export type CoverInstanceMethods<T> = {
    [M in MethodNames<T> as `cover${Capitalize<M>}`]: CoverInstanceMethod<T, M>;
};

export type CoverStaticMethods<TStatic> = {
    [M in MethodNames<TStatic> as `cover${Capitalize<M>}`]: CoverStaticMethod<
        TStatic,
        M
    >;
};

export type CoverCtors<T extends abstract new (...args: any) => any> = {
    coverConstructor: CoverCtor<T>;
};

export type CoverCtor<T extends abstract new (...args: any) => any> = {
    (spec: (this: CoverCtorContext<T>) => void): void;
};

export type CoverInstanceMethod<T, M extends MethodNames<T>> = {
    (spec: (this: CoverMethodContext<T, M>) => void): void;
};

export type CoverStaticMethod<T, M extends MethodNames<T>> = {
    (spec: (this: CoverMethodContext<T, M>) => void): void;
};

export type CoverCtorContext<T extends abstract new (...args: any) => any> =
    CoverCtorContextFunc<T> & CoverCtorContextMembers<T>;

export type CoverCtorContextFunc<T extends abstract new (...args: any) => any> =
    {
        (...args: ConstructorParameters<T>): CtorCall<T>;
    };

export type CoverCtorContextMembers<
    T extends abstract new (...args: any) => any,
> = {
    _should(expectation: string, spec: () => void): void;

    whenCalled(call: CtorCall<T>): CtorCallFactBuilder<T>;
    whenCalled(...calls: CtorCall<T>[]): CtorCallTheoryBuilder<T>;
};

export type CoverMethodContext<
    T,
    M extends MethodNames<T>,
> = CoverMethodContextFunc<T, M> & CoverMethodContextMembers<T, M>;

export type CoverMethodContextFunc<T, M extends MethodNames<T>> = {
    (...args: MethodArgs<T, M>): Call<T, M>;
};

export type CoverMethodContextMembers<T, M extends MethodNames<T>> = {
    _should(expectation: string, spec: () => void): void;

    whenCalled(call: Call<T, M>): CallFactBuilder<T, M>;
    whenCalled(...calls: Call<T, M>[]): CallTheoryBuilder<T, M>;
};

export interface CallFactBuilder<T, M extends MethodNames<T>> {
    _should(expectation: string, spec: (call: Call<T, M>) => void): void;
}

export interface CtorCallFactBuilder<
    T extends abstract new (...args: any) => any,
> {
    _should(expectation: string, spec: (call: CtorCall<T>) => void): void;
}

export interface CallTheoryBuilder<T, M extends MethodNames<T>> {
    shouldAll(expectation: string, spec: (call: Call<T, M>) => void): void;
}

export interface CtorCallTheoryBuilder<
    T extends abstract new (...args: any) => any,
> {
    shouldAll(expectation: string, spec: (call: CtorCall<T>) => void): void;
}

export type Call<T, M extends MethodNames<T>> = CallFunc<T, M> &
    CallMembers<T, M>;

type CallFunc<T, M extends MethodNames<T>> = {
    (instance: T): T[M] extends (...args: any) => infer R ? R : never;
};

type CallMembers<T, M extends MethodNames<T>> = {
    method: M;
    args: MethodArgs<T, M>;
};

export type CtorCall<T extends abstract new (...args: any) => any> =
    CtorCallFunc<T> & CtorCallMembers<T>;

type CtorCallFunc<T extends abstract new (...args: any) => any> = {
    (): T;
};

type CtorCallMembers<T extends abstract new (...args: any) => any> = {
    args: ConstructorParameters<T>;
};

class CallFactTheoryImpl<T, M extends MethodNames<T>>
    implements CallFactBuilder<T, M>, CallTheoryBuilder<T, M>
{
    constructor(
        private readonly _type: PublicCtor<T>,
        private readonly _method: M,
        private readonly _calls: Call<T, M>[],
    ) {}

    _should(expectation: string, spec: (call: Call<T, M>) => void): void {
        const type = this._type.name;
        const method = this._method;
        const args = _jsargs(this._calls[0].args);
        const fullExpectation = `🌲 "${type}"'s 🌿 "${method}" method should ${expectation} when called with ${args}`;

        it(fullExpectation, () => spec(this._calls[0]));
    }

    shouldAll(expectation: string, spec: (call: Call<T, M>) => void): void {
        const type = this._type.name;
        const method = this._method;
        const theory = `🌲 "${type}"'s 🌿 "${method}" method should ${expectation}`;

        describe(theory, () => {
            for (const call of this._calls) {
                const args = _jsargs(call.args);
                const fullExpectation = `${theory} when called with ${args}`;

                it(fullExpectation, () => spec(call));
            }
        });
    }
}

export class CoverTypeContextFactory {
    public static create<
        T extends abstract new (...args: any) => any,
        TStatic extends abstract new (...args: any) => T,
    >(type: PublicCtor<T>): CoverTypeContext<T, TStatic> {
        let result = {} as any;

        for (const key of Object.getOwnPropertyNames(type.prototype)) {
            if (
                key === 'constructor' ||
                typeof type.prototype[key] !== 'function'
            ) {
                continue;
            }

            const capitalizedKey = `${key[0].toUpperCase()}${key.substring(1)}`;

            result[`cover${capitalizedKey}`] =
                CoverTypeContextFactory.createMethod<T, any>(
                    type,
                    key,
                    'instance',
                );
        }

        for (const key of Object.getOwnPropertyNames(type)) {
            if (typeof (type as any)[key] !== 'function') {
                continue;
            }

            const capitalizedKey = `${key[0].toUpperCase()}${key.substring(1)}`;

            result[`cover${capitalizedKey}`] =
                CoverTypeContextFactory.createMethod<T, any>(
                    type,
                    key,
                    'static',
                );
        }

        return result as any;
    }

    private static createMethod<T, M extends MethodNames<T>>(
        type: PublicCtor<T>,
        method: M,
        kind: 'instance' | 'static',
    ): Function {
        return function (spec: (this: CoverMethodContext<T, M>) => void) {
            const methodContext = ContextFactory.create<T, M>(type, method);

            describe(`🌿 "${method}"`, () => {
                spec.call(methodContext);
            });
        };
    }
}

class ContextFactory {
    public static create<T, M extends MethodNames<T>>(
        type: PublicCtor<T>,
        method: M,
    ): CoverMethodContext<T, M> {
        return ContextFactory.constructFrom({
            func: (...args: MethodArgs<T, M>): Call<T, M> => {
                return CallImplFactory.create<T, M>(method, args);
            },
            members: {
                _should(expectation: string, spec: () => void): void {
                    const fullExpectation = `🌲 "${type.name}"'s 🌿 "${method}" method should ${expectation}`;

                    it(fullExpectation, spec);
                },
                whenCalled(
                    ...calls: Call<T, M>[]
                ): CallFactBuilder<T, M> | CallTheoryBuilder<T, M> | any {
                    if (calls.length === 0) {
                        throw new ArgumentOutOfRangeError(nameof({ calls }));
                    }

                    if (calls.length === 1) {
                        return new CallFactTheoryImpl<T, M>(type, method, [
                            calls[0] as Call<T, M>,
                        ]);
                    }

                    return new CallFactTheoryImpl<T, M>(
                        type,
                        method,
                        calls as Call<T, M>[],
                    );
                },
            },
        });
    }

    private static constructFrom<T, M extends MethodNames<T>>(parts: {
        func: CoverMethodContextFunc<T, M>;
        members: CoverMethodContextMembers<T, M>;
    }): CoverMethodContext<T, M> {
        return Object.assign(parts.func, parts.members);
    }
}

class CallImplFactory {
    public static create<T, M extends MethodNames<T>>(
        method: M,
        args: MethodArgs<T, M>,
    ): Call<T, M> {
        return this.constructFrom({
            func: instance => (instance as any)[method](...args),
            members: { method, args },
        });
    }

    private static constructFrom<T, M extends MethodNames<T>>(parts: {
        func: CallFunc<T, M>;
        members: CallMembers<T, M>;
    }) {
        return Object.assign(parts.func, parts.members);
    }
}

class CoverMethodContextImpl<T, M extends MethodNames<T>> {
    // implements CoverMethodContext<T, M>
    constructor(
        private readonly _type: PublicCtor<T>,
        private readonly _method: M,
    ) {}

    should(expectation: string, spec: () => void) {
        const fullExpectation = `🌲 "${this._type.name}"'s 🌿 "${this._method}" method should ${expectation}`;

        it(fullExpectation, spec);
    }
}
