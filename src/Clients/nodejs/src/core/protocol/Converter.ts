import { RpcMessage, RpcError } from './rpc-channels';
import { TimeSpan, argumentIs, TimeoutError } from '@foundation';
import { RemoteError, Exception } from './RemoteError';

/* @internal */
export class Converter {
    public static toRpcRequest(endpoint: string, methodName: string, args: unknown[], timeout: TimeSpan): RpcMessage.Request {
        const serializedArgs = args.map(arg => JSON.stringify(arg));
        return new RpcMessage.Request(timeout.totalSeconds, endpoint, methodName, serializedArgs);
    }

    public static fromRpcResponse(response: RpcMessage.Response, request: RpcMessage.Request): unknown {
        if (response.Error) {
            throw Converter.createRemoteError(request, response.Error);
        } else {
            if (response.Data == null) {
                return undefined;
            } else {
                return JSON.parse(response.Data);
            }
        }
    }

    public static createRemoteError(request: RpcMessage.Request, error: RpcError): Error {
        if (error.Type === 'System.TimeoutException') {
            return new TimeoutError({ reportedByServer: true });
        }

        return new RemoteError(
            request.Endpoint,
            request.MethodName,
            createException(error),
        );

        function createException(x: RpcError): Exception {
            return {
                type: x.Type,
                message: x.Message,
                stackTrace: x.StackTrace,
                innerException: x.InnerError ? createException(x.InnerError) : undefined,
            };
        }
    }

    public static toRpcError(err: any): RpcError {
        argumentIs(err, 'err', 'string', Object);

        if (err instanceof Error) {
            return new RpcError(err.message, err.stack ?? '', err.name, null);
        } else {
            return new RpcError(`${err}`, '', '', null);
        }
    }
}
