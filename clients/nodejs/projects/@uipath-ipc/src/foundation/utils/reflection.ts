export interface PublicConstructor<T> {
    readonly name: string;
    readonly prototype: any;
    new(...args: any[]): T;
}

export type Method = (...args: any[]) => any;

export type MemberMethod = (this: MethodContainer, ...args: any[]) => any;

export interface MethodContainer { readonly [methodName: string]: MemberMethod | undefined; }
