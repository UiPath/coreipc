export interface PublicCtor<T = unknown> {
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

declare global {
    interface Object {
        become<T>(this: any, type: new (...args: any[]) => T): T;
    }
}

interface Becomable<T> {
    __proto__: any;
    constructor: new (...args: any[]) => T;
}

Object.prototype.become = function <T>(this: any, type: new (...args: any[]) => T): T {
    const me = this as Becomable<T>;
    me.__proto__ = type.prototype;
    me.constructor = type;
    return this;
};
