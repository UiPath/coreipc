import { ArgumentOutOfRangeError, nameof, PublicCtor } from '../../../src/std';
import { MethodArgs, MethodNames } from './funky';

const originalIt = globalThis.it;
const originalDescribe = globalThis.describe;

declare global {
    function describe<
        TStatic extends AbstractCtor,
        TInstance extends InstanceType<TStatic>,
    >(type: Cover<TStatic, TInstance>, specDefinitions: () => void): void;

    function describe<
        TStatic extends AbstractCtor,
        TInstance extends InstanceType<TStatic>,
        TMethod extends MethodNames<TInstance>,
    >(
        method: Title.Method.Instance<TStatic, TInstance, TMethod>,
        specDefinitions: (
            this: Title.Method.Instance<TStatic, TInstance, TMethod>,
        ) => void,
    ): void;
}

const stack = new Array<string>();
const kind = Symbol();

type Kinds = 'instance-method' | 'static-method' | 'ctor';

type Marked<Kind extends Kinds> = {
    [kind]: Kind;
};

function ensureUnique(description: string): string {
    if (ensureUnique.Seen.indexOf(description) === -1) {
        ensureUnique.Seen.push(description);
        return description;
    }

    const maybeIndexAsString =
        ensureUnique.Regex.exec(description)?.groups?.['index'];

    let index = 1;
    if (maybeIndexAsString) {
        index = parseInt(maybeIndexAsString) + 1;
    }

    let candidate = `${description} (${index})`;
    do {
        if (ensureUnique.Seen.indexOf(candidate) === -1) {
            ensureUnique.Seen.push(candidate);
            return candidate;
        }
    } while (true);
}
module ensureUnique {
    export const Seen = new Array<string>();

    export const Regex = /^.+\((?<index>\d+)\)$/s;
}

export type Description = string | Title.Method.Instance<any, any, any>;

globalThis.describe = function (
    description: any,
    specDefinitions: () => void,
): void {
    let _this: any = undefined;

    if ((description as any)[kind] === <Kinds>'instance-method') {
        _this = description;
    }

    const tail = stack.join('.');
    stack.push(description.toString());

    const newDescription = [tail, description].filter(x => !!x).join('.');
    const uniqueDescription = ensureUnique(newDescription);

    try {
        (originalDescribe as any).call(this, uniqueDescription, () =>
            specDefinitions.call(_this),
        );
    } finally {
        stack.pop();
    }
};

globalThis.it = function (
    expectation: string,
    assertion?: jasmine.ImplementationCallback,
    timeout?: number,
): void {
    const tail = stack.join('.');
    stack.push(expectation);

    const newDescription = [tail, expectation].filter(x => !!x).join(' ');
    const uniqueDescription = ensureUnique(newDescription);

    try {
        originalIt.call(this, uniqueDescription, assertion, timeout);
    } finally {
        stack.pop();
    }
};

module echo {
    export function create<TArgs extends any[]>() {
        return function <TArgs extends any[]>(...args: TArgs): TArgs {
            return args;
        };
    }
}

export function cover<TStatic extends AbstractCtor>(
    type: PublicCtor<InstanceType<TStatic>>,
): Cover<TStatic, InstanceType<TStatic>> {
    const instancePairs = Title.methods<TStatic, InstanceType<TStatic>>(
        type,
        'instance',
    ).map(key => ({
        [`$${Title.capitalize(key)}`]: Title.Method.Instance.create<
            TStatic,
            InstanceType<TStatic>,
            any
        >(type, key),
    }));

    const staticPairs = Title.methods(type, 'static').map(key => ({
        [`$${Title.capitalize(key)}`]: Title.Method.Static.create(type, key),
    }));

    const ctorPair = {
        $Constructor: Title.Ctor.create(type),
    };

    const result = Object.assign(
        `🌲${type.name}` as any,
        ctorPair,
        ...instancePairs,
        ...staticPairs,
    );

    return result;
}

type AbstractCtor = abstract new (...args: any) => any;

export type Cover<
    TStatic extends AbstractCtor,
    TInstance extends InstanceType<TStatic>,
> = string &
    Cover.Ctors<TStatic, TInstance> &
    Cover.Methods<TStatic, TInstance>;

export type Title<
    TStatic extends AbstractCtor,
    TInstance extends InstanceType<TStatic>,
    TKey extends Key<TStatic, TInstance>,
> = string & {
    readonly foo: string;
};

export module Title {
    export function methods<
        TStatic extends AbstractCtor,
        TInstance extends InstanceType<TStatic>,
    >(
        type: PublicCtor<TInstance>,
        kind: 'instance' | 'static',
    ): Key<TStatic, TInstance>[] {
        if (kind === 'instance') {
            const names = Object.getOwnPropertyNames(type.prototype);
            return names as any;
        }

        if (kind === 'static') {
            const names = Object.getOwnPropertyNames(type);
            return names as any;
        }

        throw new ArgumentOutOfRangeError(nameof({ kind }));
    }

    export function capitalize(x: string): string {
        if (!x) {
            return x;
        }

        return `${x.charAt(0).toUpperCase()}${x.slice(1)}`;
    }

    export type Ctor<
        TStatic extends AbstractCtor,
        TInstance extends InstanceType<TStatic>,
    > = Marked<'ctor'> &
        Title<TStatic, TInstance, 'constructor'> & {
            bar: string;
        };

    export module Ctor {
        export function create<
            TStatic extends AbstractCtor,
            TInstance extends InstanceType<TStatic>,
        >(type: PublicCtor<TInstance>): Ctor<TStatic, TInstance> {
            const title = `🌱ctor`;

            return Object.assign(title, {
                [kind]: <Kinds>'ctor',
                foo: 'foo',
                bar: 'bar',
            }) as any;
        }
    }

    export type Method<
        TStatic extends AbstractCtor,
        TInstance extends InstanceType<TStatic>,
        TKey extends Key<TStatic, TInstance>,
    > = Title<TStatic, TInstance, TKey> & {
        frob: string;
    };

    export module Method {
        export type Instance<
            TStatic extends AbstractCtor,
            TInstance extends InstanceType<TStatic>,
            TMethod extends MethodNames<TInstance>,
        > = Marked<'instance-method'> &
            Method<TStatic, TInstance, TMethod> & {
                (...args: MethodArgs<TInstance, TMethod>): MethodArgs<
                    TInstance,
                    TMethod
                >;
                alpha: string;
            };

        export module Instance {
            export function create<
                TStatic extends AbstractCtor,
                TInstance extends InstanceType<TStatic>,
                TMethod extends MethodNames<TInstance>,
            >(
                type: PublicCtor<TInstance>,
                method: TMethod,
            ): Instance<TStatic, TInstance, TMethod> {
                const title = `🌿${method}`;

                const func = echo.create<MethodArgs<TInstance, TMethod>>();

                return Object.assign(func, {
                    [kind]: <Kinds>'instance-method',
                    toString(): string {
                        return title;
                    },
                    foo: 'foo',
                    bar: 'bar',
                    frob: 'frob',
                    alpha: 'alpha',
                }) as any;
            }
        }

        export type Static<
            TStatic extends AbstractCtor,
            TInstance extends InstanceType<TStatic>,
            TKey extends Key<TStatic, TInstance>,
        > = Marked<'static-method'> &
            Method<TStatic, TInstance, TKey> & {
                bravo: string;
            };

        export module Static {
            export function create<
                TStatic extends AbstractCtor,
                TInstance extends InstanceType<TStatic>,
                TKey extends Key<TStatic, TInstance>,
            >(
                type: PublicCtor<TInstance>,
                key: TKey,
            ): Static<TStatic, TInstance, TKey> {
                const title = `🌿${key}`;

                return Object.assign(title, {
                    [kind]: <Kinds>'static-method',
                    foo: 'foo',
                    bar: 'bar',
                    frob: 'frob',
                    bravo: 'bravo',
                }) as any;
            }
        }
    }
}

export type Key<
    TStatic extends AbstractCtor,
    TInstance extends InstanceType<TStatic>,
> = 'constructor' | MethodNames<TInstance> | MethodNames<TStatic>;

export module Cover {
    export type Ctors<
        TStatic extends AbstractCtor,
        TInstance extends InstanceType<TStatic>,
    > = {
        $Constructor: Title.Ctor<TStatic, TInstance>;
    };

    export type Methods<
        TStatic extends AbstractCtor,
        TInstance extends InstanceType<TStatic>,
    > = Methods.Instance<TStatic, TInstance> &
        Methods.Static<TStatic, TInstance>;

    export module Methods {
        export type Instance<
            TStatic extends AbstractCtor,
            TInstance extends InstanceType<TStatic>,
        > = {
            [M in MethodNames<TInstance> as `$${Capitalize<M>}`]: Title.Method.Instance<
                TStatic,
                TInstance,
                M
            >;
        };

        export type Static<
            TStatic extends AbstractCtor,
            TInstance extends InstanceType<TStatic>,
        > = {
            [M in MethodNames<TStatic> as `$${Capitalize<M>}`]: Title.Method.Static<
                TStatic,
                TInstance,
                M
            >;
        };
    }
}
