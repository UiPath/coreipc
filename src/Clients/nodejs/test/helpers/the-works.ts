import { TimeSpan, JsonConvert } from '@foundation';
import { assert, expect, spy, use } from 'chai';
import chaiAsPromised from 'chai-as-promised';
import spies from 'chai-spies';
import 'chai/register-should';
import * as util from 'util';

use(spies);
use(chaiAsPromised);

export function calling<TFunction extends (...args: any[]) => any>(
    f: TFunction,
    ...args: Parameters<TFunction>): () => ReturnType<TFunction> {
    return () => f(...args);
}

export function constructing<TConstructor extends new (...args: any[]) => any>(
    ctor: TConstructor, ...args: ConstructorParameters<TConstructor>): () => InstanceType<TConstructor> {
    return () => new ctor(...args);
}

export function forInstance<TInstance>(obj: TInstance): ForInstance<TInstance> {
    return new ForInstance(obj);
}

export class ForInstance<TInstance> {
    constructor(private readonly _obj: TInstance) { }

    public spyOn<Key extends keyof TInstance>(methodName: Key & string): TInstance[Key] {
        const bound = (this._obj[methodName] as unknown as (...args: any[]) => any).bind(this._obj);
        return spy(bound) as any;
    }

    public calling<Key extends keyof TInstance>(
        methodName: Key & string,
        ...args: TInstance[Key] extends (...args: any[]) => any
            ? Parameters<TInstance[Key]>
            : never): () => any {

        return () => {
            const method = this._obj[methodName] as unknown as (...args: any[]) => any;
            return method.bind(this._obj)(...args);
        };
    }

    public callingWrong<Key extends keyof TInstance>(
        methodName: Key & string,
        ...args: never[]): () => any {

        return () => {
            const method = this._obj[methodName] as unknown as (...args: any[]) => any;
            return method.bind(this._obj)(...args);
        };
    }
}

export function toJavaScript(x: unknown): string {
    switch (typeof x) {
        case 'undefined': return 'undefined';
        case 'function': return x.toString();
        case 'symbol': return x.toString();
        case 'object':
            if (x instanceof Error && x.constructor) {
                return `new ${x.constructor.name}('${x.message}')`;
            }
            if (x instanceof TimeSpan && x.isInfinite) {
                return 'Timeout.infiniteTimeSpan';
            }
            if (x instanceof Uint8Array && x.constructor) {
                return `${x.constructor.name}[byteLength: ${x.byteLength}]`;
            }
            if (x && !(x instanceof Array) && x.constructor && x.constructor !== Object) {
                if (x.toString !== Object.prototype.toString) { return x.toString(); }
                return `new ${x.constructor.name}(...)`;
            }
            break;
    }
    return JsonConvert.serializeObject(x);
}

export function concatArgs(args: readonly unknown[]): string {
    return args.map(toJavaScript).join(', ');
}

export function asParamsOf<T extends (...args: any[]) => any>(_func: T): (...args: Parameters<T>) => Parameters<T> {
    return (...args) => args;
}

export async function waitAllAndPrintAnyErrors(...promises: Array<Promise<unknown>>): Promise<void> {
    try {
        await Promise.all(promises);
    } catch { }

    for (const promise of promises) {
        try {
            await promise;
        } catch (err) {
            console.error('>>>> Caught error: ', util.inspect(err, {
                colors: true,
                depth: null,
                maxArrayLength: null,
            }), '\r\n');
        }
    }
}

export {
    expect,
    spy,
    assert,
};
