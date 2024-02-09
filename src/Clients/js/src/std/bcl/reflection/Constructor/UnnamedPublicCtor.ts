export interface UnnamedPublicCtor<T = unknown> {
    new (...args: any[]): T;
}
