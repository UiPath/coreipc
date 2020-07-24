import { RpcMessage, IpcError } from './rpc-channels';
import { TimeSpan, argumentIs, TimeoutError, InvalidOperationError, Trace } from '../../foundation';
import { RemoteError, Exception } from './RemoteError';

/* @internal */
export class Converter {
    private static readonly _trace = Trace.category('Converter');

    public static toRpcRequest(endpoint: string, methodName: string, args: unknown[], timeout: TimeSpan): RpcMessage.Request {
        let serializedArgs: string[] | undefined;
        let result: RpcMessage.Request | undefined;
        let error: Error | undefined;

        try {
            serializedArgs = args.map(arg => JSON.stringify(arg));
            result = new RpcMessage.Request(timeout.totalSeconds, endpoint, methodName, serializedArgs);
            trace();
            return result;
        } catch (err) {
            error = err;
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

    public static fromRpcResponse(response: RpcMessage.Response, request: RpcMessage.Request): unknown {
        if (response.Error) {
            throw Converter.createRemoteError(request, response.Error);
        } else {
            if (!response.Data) {
                return undefined;
            } else {
                try {
                    return JSON.parse(response.Data);
                } catch (err) {
                    const ioe = new InvalidOperationError(`Failed to parse JSON data for response of request ${request.MethodName}. Data was:\r\n${response.Data}`);
                    throw ioe;
                }
            }
        }
    }

    public static createRemoteError(request: RpcMessage.Request, error: IpcError): Error {
        if (error.Type === 'System.TimeoutException') {
            return new TimeoutError({ reportedByServer: true });
        }

        return new RemoteError(
            request.Endpoint,
            request.MethodName,
            createException(error),
        );

        function createException(x: IpcError): Exception {
            return {
                type: x.Type,
                message: x.Message,
                stackTrace: x.StackTrace,
                innerException: x.InnerError ? createException(x.InnerError) : undefined,
            };
        }
    }

    public static toRpcError(err: any): IpcError {
        argumentIs(err, 'err', 'string', Object);

        if (err instanceof Error) {
            return new IpcError(err.message, err.stack ?? '', err.name, null);
        } else {
            return new IpcError(`${err}`, '', '', null);
        }
    }
}
