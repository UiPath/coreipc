import {
    TimeSpan,
    TimeoutError,
    InvalidOperationError,
    Trace,
    UnknownError,
} from '../../../bcl';

import { RemoteError, Exception, RpcMessage, IpcError } from '..';

/* @internal */
export class Converter {
    private static readonly _trace = Trace.category('Converter');

    public static toRpcRequest(
        endpoint: string,
        methodName: string,
        args: unknown[],
        timeout: TimeSpan,
    ): RpcMessage.Request {
        let serializedArgs: string[] | undefined;
        let result: RpcMessage.Request | undefined;
        let error: Error | undefined;

        try {
            serializedArgs = args.map((arg) => JSON.stringify(arg));
            result = new RpcMessage.Request(
                timeout.totalSeconds,
                endpoint,
                methodName,
                serializedArgs,
            );
            trace();
            return result;
        } catch (err) {
            error = UnknownError.ensureError(err);
            trace();
            throw err;
        }

        function trace(): void {
            Converter._trace.log({
                $type: 'Converter',
                $operation: 'toRpcRequest',
                $details: {
                    input: {
                        endpoint,
                        methodName,
                        args,
                        timeout,
                    },
                    middle: {
                        serializedArgs,
                    },
                    output: {
                        result,
                        error,
                    },
                },
            });
        }
    }

    public static unwrapRpcResponse(response: RpcMessage.Response, request: RpcMessage.Request): unknown {
        if (response.Error) {
            throw Converter.createRemoteError(request, response.Error);
        } else {
            if (!response.Data) {
                return undefined;
            } else {
                try {
                    return JSON.parse(response.Data);
                } catch (err) {
                    const ioe = new InvalidOperationError(
                        `Failed to parse JSON data for response of request ${request.MethodName}. Data was:\r\n${response.Data}`,
                    );
                    throw ioe;
                }
            }
        }
    }

    public static createRemoteError(request: RpcMessage.Request, error: IpcError): Error {
        if (error.Type === 'System.TimeoutException') {
            return new TimeoutError({ reportedByServer: true });
        }

        return new RemoteError(request.Endpoint, request.MethodName, createException(error));

        function createException(x: IpcError): Exception {
            return {
                type: x.Type,
                message: `${x.Type}: "${x.Message}"`,
                stackTrace: x.StackTrace,
                inner: x.InnerError ? createException(x.InnerError) : undefined,
            };
        }
    }

    /** A callback can throw anything, even a value whose properties or conversion throw: the caller still gets an error. */
    public static toRpcError(err: unknown): IpcError {
        try {
            if (err instanceof Error) {
                // String(): an Error's message or name can be anything, and the response must still be written.
                return new IpcError(String(err.message), String(err.stack ?? ''), String(err.name), null);
            }
            return new IpcError(String(err), '', '', null);
        } catch {
            let message: string;
            try {
                message = Object.prototype.toString.call(err);
            } catch {
                message = typeof err;
            }
            return new IpcError(message, '', '', null);
        }
    }
}
