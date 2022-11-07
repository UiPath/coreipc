export interface PublicCtor<T = unknown> {
    readonly name?: string;
    readonly prototype: any;

    new(...args: any[]): T;
}

export interface NamedPublicCtor<T = unknown> {
    readonly name: string;
    readonly prototype: any;

    new(...args: any[]): T;
}

export enum Primitive {
    string = 'string',
    number = 'number',
    bigint = 'bigint',
    boolean = 'boolean',
    void = 'undefined',
}

interface Becomable<T> {
    __proto__: any;
    constructor: new (...args: any[]) => T;
}

export class ObjectPal {
    public static become<T>(obj: any, type: new (...args: any[]) => T): T {
        const me = obj as Becomable<T>;
        me.__proto__ = type.prototype;
        me.constructor = type;
        return obj;
    }
}
