export interface PublicConstructor<T> {
    readonly name: string;
    readonly prototype: any;
    new(...args: any[]): T;
}

/* @internal */
export type Method = (...args: any[]) => any;

/* @internal */
export type MemberMethod = (this: MethodContainer, ...args: any[]) => any;

/* @internal */
export interface MethodContainer { readonly [methodName: string]: MemberMethod | undefined; }
