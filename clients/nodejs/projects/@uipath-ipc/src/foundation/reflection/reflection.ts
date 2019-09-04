export interface PublicConstructor<T> {
    readonly name: string;
    readonly prototype: any;
    new(...args: any[]): T;
}

/* @internal */
export type Method = (...args: any[]) => any;
