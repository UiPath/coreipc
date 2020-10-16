/* @internal */
export interface ICallInterceptor<TContract = unknown> {
    invokeMethod(methodName: string & keyof TContract, args: unknown[]): Promise<unknown>;
}
