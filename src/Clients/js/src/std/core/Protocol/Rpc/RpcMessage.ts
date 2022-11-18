import { JsonConvert } from '../../../bcl';
import { Network, IpcError } from '..';
import { RpcMessageBase } from '.';

/* @internal */
export type RpcMessage = RpcMessage.Request | RpcMessage.Response | RpcMessage.CancellationRequest;

/* @internal */
export module RpcMessage {
    export class Request extends RpcMessageBase {
        public constructor(
            public readonly TimeoutInSeconds: number,
            public readonly Endpoint: string,
            public readonly MethodName: string,
            public readonly Parameters: string[],
        ) {
            super();
        }

        public Id: string = '';

        public toNetwork(): Network.Message {
            return {
                type: Network.Message.Type.Request,
                data: Buffer.from(JsonConvert.serializeObject(this)),
            };
        }

        public static fromNetwork(message: Network.Message): Request {
            return JsonConvert.deserializeObject(message.data.toString(), Request);
        }
    }

    export class Response extends RpcMessageBase {
        public static fromNetwork(message: Network.Message): Response {
            return JsonConvert.deserializeObject(message.data.toString(), Response);
        }

        public constructor(
            public RequestId: string,
            public readonly Data: string | null,
            public readonly Error: IpcError | null,
        ) {
            super();
        }

        public toNetwork(): Network.Message {
            return {
                type: Network.Message.Type.Response,
                data: Buffer.from(JsonConvert.serializeObject(this)),
            };
        }
    }

    export class CancellationRequest extends RpcMessageBase {
        public static fromNetwork(message: Network.Message): CancellationRequest {
            return JsonConvert.deserializeObject(message.data.toString(), CancellationRequest);
        }

        public constructor(public readonly RequestId: string) {
            super();
        }

        public toNetwork(): Network.Message {
            return {
                type: Network.Message.Type.Cancel,
                data: Buffer.from(JsonConvert.serializeObject(this)),
            };
        }
    }
}
