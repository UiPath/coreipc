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
