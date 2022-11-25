export * from './__members';
export * from './cover2';

import { assert, expect, use } from 'chai';
import chai from 'chai';
import chaiAsPromised from 'chai-as-promised';
import { JsonConvert, TimeSpan } from '../../src/std';

chai.should();
use(chaiAsPromised);

export * from './__';

export function constructing<TConstructor extends new (...args: any[]) => any>(
    ctor: TConstructor,
    ...args: ConstructorParameters<TConstructor>
): () => InstanceType<TConstructor> {
    return () => new ctor(...args);
}

export function calling<TFunction extends (...args: any[]) => any>(
    f: TFunction,
    ...args: Parameters<TFunction>
): () => ReturnType<TFunction> {
    return () => f(...args);
}

export function toJavaScript(x: unknown): string {
    switch (typeof x) {
        case 'undefined':
            return 'undefined';
        case 'function':
            return x.toString();
        case 'symbol':
            return x.toString();
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
                if (x.toString !== Object.prototype.toString) {
                    return x.toString();
                }
                return `new ${x.constructor.name}(...)`;
            }
            break;
    }
    return JsonConvert.serializeObject(x);
}

export function _jsargs(args: readonly unknown[]): string {
    return args.map(toJavaScript).join(', ');
}

const context = describe;

export { expect, assert, context };
