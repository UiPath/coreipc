/* @internal */
export interface ICallInterceptor<TService> {
    invokeMethod(methodName: string & keyof TService, args: unknown[]): Promise<unknown>;
}
