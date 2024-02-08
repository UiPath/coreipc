import { UnnamedPublicCtor } from './Constructor';

export class ObjectPal {
    public static become<T>(obj: any, type: UnnamedPublicCtor<T>): T {
        const me = obj as Becomable<T>;
        me.__proto__ = type.prototype;
        me.constructor = type;
        return obj;
    }
}

interface Becomable<T> {
    __proto__: any;
    constructor: new (...args: any[]) => T;
}
