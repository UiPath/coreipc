export interface PublicCtor<T = unknown> {
    readonly name: string;
    readonly prototype: any;

    new (...args: any[]): T;
}
