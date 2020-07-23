import { RpcMessage, IpcError } from './rpc-channels';
import { TimeSpan, argumentIs, TimeoutError } from '../../foundation';
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
