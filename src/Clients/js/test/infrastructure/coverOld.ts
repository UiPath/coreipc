import { _jsargs } from '.';
import {
    ArgumentError,
    ArgumentOutOfRangeError,
    InvalidOperationError,
    nameof,
    PublicCtor,
} from '../../src/std';
import { OverloadedParameters } from './overloadedParameters';

export interface ICover {
    type<T>(type: PublicCtor<T>): ICover.IType<T>;
}

export function calling<T, K extends ICover.MethodNames<T>>(
    preparedCall: ICover.IType.MethodPreparedCall<T, K>,
): ICover.IType.MethodFact<T, K>;
export function calling<T, K extends ICover.MethodNames<T>>(
    ...preparedCalls: ICover.IType.MethodPreparedCall<T, K>[]
): ICover.IType.MethodTeory<T, K>;
export function calling<T, K extends ICover.MethodNames<T>>(
    preparedCalls:
        | ICover.IType.MethodPreparedCall<T, K>[]
        | ICover.IType.MethodPreparedCall<T, K>,
): ICover.IType.MethodFact<T, K> | ICover.IType.MethodTeory<T, K> {
    if (preparedCalls instanceof Cover.MethodPreparedCall) {
        return new Cover.Calling<T, K>([preparedCalls as any]);
    }

    if (preparedCalls instanceof Array) {
        return new Cover.Calling<T, K>(preparedCalls as any);
    }

    throw new ArgumentOutOfRangeError(nameof({ preparedCalls }));
}

export module ICover {
    export interface IType<T> {
        thusly(how: IType.IHow<T>): void;
    }

    export type MethodNames<T> = keyof Where<T, string, (...args: any[]) => any>;

    export type MethodArgs<T, K extends MethodNames<T>> = T[K] extends (
        ...args: any[]
    ) => any
        ? OverloadedParameters<T[K]>
        : never;

    export type MethodArgsSimple<T, K extends MethodNames<T>> = T[K] extends (
        ...args: any[]
    ) => any
        ? Parameters<T[K]>
        : never;

    export type Where<
        Source,
        KeyCondition extends string | number | symbol,
        ValueCondition,
    > = Pick<
        Source,
        {
            [K in KeyCondition & keyof Source]: Source[K] extends ValueCondition
                ? K
                : never;
        }[KeyCondition & keyof Source]
    >;

    export module IType {
        export interface ICtor<T> {}

        export interface MethodSpecContext<T, K extends MethodNames<T>> {
            (...args: MethodArgs<T, K>): MethodPreparedCall<T, K>;
        }

        export interface MethodPreparedCall<T, K extends MethodNames<T>> {
            //
        }

        export interface MethodFact<T, K extends MethodNames<T>> {
            should(description: string): MethodCallSpec<T, K>;
        }

        export interface MethodTeory<T, K extends MethodNames<T>> {
            shouldAll(description: string): MethodCallSpec<T, K>;
        }

        export interface MethodCallSpec<T, K extends MethodNames<T>> {
            moreSpecifically(spec: (args: MethodArgsSimple<T, K>) => void): void;
        }

        export type MethodSpec<T, K extends MethodNames<T>> = (
            this: MethodSpecContext<T, K>,
        ) => void;

        export type IHow<T> = IHowMethods<T> & {
            ctor?: boolean;
        };

        export type IHowMethods<T> = {
            [key in MethodNames<T> as `instance_method_${key}`]?: MethodSpec<T, key>;
        };
    }
}

class Cover implements ICover {
    type<T>(type: PublicCtor<T>): ICover.IType<T> {
        return new Cover.Type<T>(type);
    }
}

module Cover {
    export class Calling<T, K extends ICover.MethodNames<T>>
        implements ICover.IType.MethodFact<T, K>, ICover.IType.MethodTeory<T, K>
    {
        private readonly _context: TypeMethodContext<T, K>;

        constructor(
            private readonly _preparedCalls: Cover.MethodPreparedCall<T, K>[],
        ) {
            const current = TypeMethodContext.current;
            if (!current) {
                throw new InvalidOperationError();
            }

            this._context = current;
        }

        should(expectation: string): ICover.IType.MethodCallSpec<T, K> {
            return new MethodCallSpec<T, K>(
                this._context,
                'should',
                expectation,
                this._preparedCalls,
            );
        }

        shouldAll(expectation: string): ICover.IType.MethodCallSpec<T, K> {
            return new MethodCallSpec<T, K>(
                this._context,
                'shouldAll',
                expectation,
                this._preparedCalls,
            );
        }
    }

    export class MethodCallSpec<T, K extends ICover.MethodNames<T>>
        implements ICover.IType.MethodCallSpec<T, K>
    {
        constructor(
            private readonly _context: TypeMethodContext<T, K>,
            private readonly _kind: 'should' | 'shouldAll',
            private readonly _expectation: string,
            private readonly _preparedCalls: Cover.MethodPreparedCall<T, K>[],
        ) {
            if (_kind !== 'should' && _kind !== 'shouldAll') {
                throw new ArgumentOutOfRangeError(nameof({ _kind }));
            }
        }

        moreSpecifically(spec: (args: ICover.MethodArgsSimple<T, K>) => void) {
            const typeDesc = typePill(this._context.type);
            const methodDesc = methodPill(this._context.type, this._context.methodName);

            if (this._kind === 'should') {
                const factDesc = `${methodDesc} should ${this._expectation} (args: ${this._preparedCalls[0]})`;

                it(factDesc, () => {
                    spec(this._preparedCalls[0].args as any);
                });

                return;
            }

            if (this._kind === 'shouldAll') {
                const theoryDesc = `${typeDesc} ${methodDesc} should ${this._expectation}`;

                describe(theoryDesc, () => {
                    for (const preparedCall of this._preparedCalls) {
                        const factDesc = `${theoryDesc} (argsr: ${preparedCall})`;
                        it(factDesc, () => {
                            spec(preparedCall.args as any);
                        });
                    }
                });

                return;
            }

            throw new InvalidOperationError();
        }
    }

    export class MethodPreparedCall<T, K extends ICover.MethodNames<T>>
        implements ICover.IType.MethodPreparedCall<T, K>
    {
        constructor(public readonly args: ICover.MethodArgs<T, K>) {}

        toString() {
            return _jsargs(this.args);
        }
    }

    export class TypeMethodContext<T, K extends ICover.MethodNames<T> = any> {
        public static current: TypeMethodContext<any, any> | undefined;

        constructor(public readonly type: PublicCtor<T>, public readonly methodName: K) {}
    }

    export function typePill<T>(type: PublicCtor<T>) {
        return `🏝️ ${type.name}'s`;
    }
    export function methodPill<T, K extends string>(type: PublicCtor<T>, key: K) {
        const info = parseMethodKey(key);

        if (info.kind !== 'instance-method' && info.kind !== 'static-method') {
            throw new ArgumentError();
        }

        return `🏝️ ${type.name}'s ${info.name} instance method`;
    }

    const RegexInstanceMethod = /^instance_method_(?<name>.+)$/s;

    const RegexStaticMethod = /^static_method_(?<name>.+)$/s;

    export function parseMethodKey(
        key: string,
    ):
        | { kind: 'ctor' }
        | { kind: 'instance-method' | 'static-method'; name: string }
        | { kind: 'unrecognized'; fullName: string } {
        if (key === 'ctor') {
            return { kind: 'ctor' };
        }

        let match = RegexInstanceMethod.exec(key);

        if (match?.groups) {
            const name = match.groups['name'];
            return { kind: 'instance-method', name };
        }

        match = RegexStaticMethod.exec(key);
        if (match?.groups) {
            const name = match.groups['name'];
            return { kind: 'static-method', name };
        }

        return { kind: 'unrecognized', fullName: key };
    }

    export class Type<T> implements ICover.IType<T> {
        constructor(private readonly _type: PublicCtor<T>) {}

        thusly(how: ICover.IType.IHow<T>) {
            describe(typePill(this._type), () => {
                for (const key of Object.keys(how)) {
                    const info = parseMethodKey(key);
                    switch (info.kind) {
                        case 'instance-method': {
                            describe(methodPill(this._type, key), () => {
                                const context = MethodSpecContextFactory.create<T, any>();

                                TypeMethodContext.current = new TypeMethodContext<T>(
                                    this._type,
                                    key,
                                );

                                ((how as any)[key] as Function).call(context);

                                TypeMethodContext.current = undefined;
                            });

                            break;
                        }
                        default: {
                        }
                    }
                }
            });
        }
    }

    export class MethodSpecContextFactory {
        public static create<
            T,
            K extends ICover.MethodNames<T>,
        >(): ICover.IType.MethodSpecContext<T, K> {
            function f(
                ...args: Parameters<ICover.IType.MethodSpecContext<T, K>>
            ): ReturnType<ICover.IType.MethodSpecContext<T, K>> {
                return new MethodPreparedCall<T, K>(args);
            }

            return f;
        }
    }

    export module Type {
        export class Builder<T> {
            constructor(private readonly _type: PublicCtor<T>) {}
        }
    }
}

export const cover: ICover = new Cover();
