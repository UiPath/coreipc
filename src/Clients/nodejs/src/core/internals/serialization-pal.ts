import * as BrokerMessage from './broker-message';
import * as WireMessage from './wire-message';
import { Message } from '../surface/message';
import { CancellationToken, CancellationTokenSource } from '../..';
import { ArgumentError } from '../../foundation/errors/argument-error';
import { TimeSpan } from '../../foundation/threading/timespan';
import { ArgumentNullError } from '../../foundation/errors/argument-null-error';
import { ICallContextDataFactory } from './context';

/* @internal */
export class SerializationPal {
    private static serializeObject(argument: any): string {
        if (argument === undefined) {
            return '';
        } else {
            return JSON.stringify(argument);
        }
    }
    private static deserializeObject(json: string): any {
        return JSON.parse(json);
    }
    private static serializeError(maybeError: Error | null | undefined): WireMessage.Error | null | undefined {
        return (undefined === maybeError) ?
            undefined :
            (null === maybeError) ?
                null :
                new WireMessage.Error(
                    maybeError.message,
                    maybeError.stack || '',
                    maybeError.name,
                    this.serializeError((maybeError as any).inner as Error | null));
    }
    private static deserializeError(maybeError: WireMessage.Error | null | undefined): Error | null | undefined {
        let result: Error | null | undefined;

        if (maybeError === undefined) {
            result = undefined;
        } else if (maybeError === null) {
            result = null;
        } else {
            result = new Error(maybeError.Message);
            result.name = maybeError.Type;
            result.stack = maybeError.StackTrace;
            const inner = this.deserializeError(maybeError.InnerError);
            if (inner !== undefined) {
                (result as any).inner = inner;
            }
        }

        return result;
    }

    public static fromJson(json: string, type: WireMessage.Type.Request): WireMessage.Request;
    public static fromJson(json: string, type: WireMessage.Type.Response): WireMessage.Response;
    public static fromJson(json: string, type: WireMessage.Type): WireMessage.Request | WireMessage.Response;
    public static fromJson(json: string, type: WireMessage.Type): WireMessage.Request | WireMessage.Response {
        if (!json) { throw new ArgumentNullError('json'); }
        if (type == null) { throw new ArgumentNullError('type'); }

        switch (type) {
            default:
                throw new ArgumentError('Unexpected WireMessage.Type', 'type');

            case WireMessage.Type.Request: {
                const result = JSON.parse(json) as WireMessage.Request;
                (result as any).__proto__ = WireMessage.Request.prototype;
                return result;
            }

            case WireMessage.Type.Response: {
                const result = JSON.parse(json) as WireMessage.Response;
                (result as any).__proto__ = WireMessage.Response.prototype;

                let error = result.Error;
                while (error) {
                    (error as any).__proto__ = WireMessage.Error.prototype;

                    error = error.InnerError;
                }

                return result;
            }
        }
    }

    public static deserializeRequest(wireRequest: WireMessage.Request): {
        brokerRequest: BrokerMessage.InboundRequest,
        id: string
    } {
        if (!wireRequest) { throw new ArgumentNullError('wireRequest'); }
        const id = wireRequest.Id;

        const args = wireRequest.Parameters.map(this.deserializeObject.bind(this));
        const brokerRequest = new BrokerMessage.InboundRequest(
            wireRequest.MethodName,
            args,
            wireRequest.TimeoutInSeconds
        );
        return { brokerRequest, id };
    }

    public static brokerResponseToWireResponse(brokerResponse: BrokerMessage.Response, id: string): WireMessage.Response {
        if (!brokerResponse) { throw new ArgumentNullError('brokerResponse'); }
        if (!id) { throw new ArgumentNullError('id'); }

        return SerializationPal.brokerResponseToWireResponseUnchecked(brokerResponse, id);
    }
    private static brokerResponseToWireResponseUnchecked(brokerResponse: BrokerMessage.Response, id: string): WireMessage.Response {
        const wireResponse = new WireMessage.Response(
            id,
            brokerResponse.maybeError ? null : this.serializeObject(brokerResponse.maybeResult),
            this.serializeError(brokerResponse.maybeError)
        );
        return wireResponse;
    }

    public static wireResponseToBuffer(wireResponse: WireMessage.Response): Buffer {
        if (!wireResponse) { throw new ArgumentNullError('wireResponse'); }

        return SerializationPal.wireResponseToBufferUnchecked(wireResponse);
    }
    private static wireResponseToBufferUnchecked(wireResponse: WireMessage.Response): Buffer {
        const json = JSON.stringify(wireResponse);
        const cbPayload = Buffer.byteLength(json, 'utf8');
        const buffer = Buffer.alloc(5 + cbPayload);

        buffer.writeUInt8(WireMessage.Type.Response as number, 0);
        buffer.writeUInt32LE(cbPayload, 1);
        buffer.write(json, 5, 'utf8');

        return buffer;
    }

    public static prepareCallContext(request: BrokerMessage.OutboundRequest, defaultTimeout: TimeSpan, ctBroker: CancellationToken): ICallContextDataFactory {
        if (!request) { throw new ArgumentNullError('request'); }
        if (!defaultTimeout) { throw new ArgumentNullError('defaultTimeout'); }
        if (defaultTimeout.isNegative) { throw new ArgumentError('Expecting a non-negative TimeSpan.', 'defaultTimeout'); }

        return SerializationPal.prepareCallContextUnchecked(request, defaultTimeout, ctBroker);
    }

    private static prepareCallContextUnchecked(request: BrokerMessage.OutboundRequest, defaultTimeout: TimeSpan, ctBroker: CancellationToken): ICallContextDataFactory {
        let timeoutSeconds = defaultTimeout.totalSeconds;
        let cancellationToken = CancellationToken.none;
        const serializedArgs = new Array<string>();

        for (const arg of request.args) {
            let wireArg: any = arg;
            if (arg instanceof Message) {
                if (arg.RequestTimeout) {
                    timeoutSeconds = arg.RequestTimeout.totalSeconds;
                }
            } else if (arg instanceof CancellationToken) {
                cancellationToken = arg;
                wireArg = null;
            }

            const jsonArg = this.serializeObject(wireArg);
            serializedArgs.push(jsonArg);
        }

        const ctsTimeout = new CancellationTokenSource();
        ctsTimeout.cancelAfter(TimeSpan.fromSeconds(timeoutSeconds));

        // const linkedToken = CancellationToken.merge(ctsTimeout.token, cancellationToken);
        cancellationToken.register(ctsTimeout.dispose.bind(ctsTimeout));

        const wireRequestFactory = (id: string) => new WireMessage.Request(
            timeoutSeconds,
            request.endpointName,
            id,
            request.methodName,
            serializedArgs
        );

        const linkedToken = CancellationToken.merge(cancellationToken, ctBroker);

        return (id: string) => ({
            cancellationToken: linkedToken,
            wireRequest: wireRequestFactory(id),
            dispose() { ctsTimeout.dispose(); }
        });
    }

    private static isRequest(obj: WireMessage.Request | WireMessage.Response): obj is WireMessage.Request { return obj instanceof WireMessage.Request; }
    private static isResponse(obj: WireMessage.Request | WireMessage.Response): obj is WireMessage.Response { return obj instanceof WireMessage.Response; }

    public static wireMessageToBuffer(wireMessage: WireMessage.Request | WireMessage.Response): Buffer {
        switch (true) {
            case wireMessage == null: throw new ArgumentNullError('wireMessage');
            case typeof wireMessage !== 'object': throw new ArgumentError(`Expecting an argument whose typeof is "object".`);
            default: throw new ArgumentError(`Expecting an argument which is either instanceof WireMessage.Request or instanceof WireMessage.Response.`, 'wireMessage');

            case SerializationPal.isRequest(wireMessage): return SerializationPal.wireRequestToBufferUnchecked(wireMessage as any);
            case SerializationPal.isResponse(wireMessage): return SerializationPal.wireResponseToBufferUnchecked(wireMessage as any);
        }
    }

    public static wireRequestToBuffer(wireRequest: WireMessage.Request): Buffer {
        if (!wireRequest) { throw new ArgumentNullError('wireRequest'); }

        return SerializationPal.wireRequestToBufferUnchecked(wireRequest);
    }
    private static wireRequestToBufferUnchecked(wireRequest: WireMessage.Request): Buffer {
        const jsonRequest = JSON.stringify(wireRequest);
        const cbPayload = Buffer.byteLength(jsonRequest);
        const buffer = Buffer.alloc(5 + cbPayload);
        buffer.writeUInt8(WireMessage.Type.Request as number, 0);
        buffer.writeInt32LE(cbPayload, 1);
        buffer.write(jsonRequest, 5, 'utf8');

        return buffer;
    }

    public static deserializeResponse(wireResponse: WireMessage.Response): {
        brokerResponse: BrokerMessage.Response,
        id: string
    } {
        if (!wireResponse) { throw new ArgumentNullError('wireResponse'); }

        const id = wireResponse.RequestId;

        const maybeError = this.deserializeError(wireResponse.Error);
        const maybeResult = wireResponse.Data ? this.deserializeObject(wireResponse.Data) : null;

        const brokerResponse = new BrokerMessage.Response(maybeResult, maybeError);
        return { brokerResponse, id };
    }
}
