import '../../jest-extensions';
import * as BrokerMessage from '../../../src/core/internals/broker/broker-message';
import * as WireMessage from '../../../src/core/internals/broker/wire-message';
import { SerializationPal } from '../../../src/core/internals/broker/serialization-pal';
import { ArgumentNullError } from '../../../src/foundation/errors/argument-null-error';
import { MockError } from '../../jest-extensions';
import { TimeSpan } from '../../../src/foundation/tasks/timespan';
import { CancellationToken, CancellationTokenSource, Message, PromisePal } from '../../../src';
import { ArgumentError } from '../../../src/foundation/errors/argument-error';

describe('Core-Internals-SerializationPal', () => {
    test(`serializeResponse throws for falsy args`, () => {
        expect(() => SerializationPal.serializeResponse(null, 'foo')).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'brokerResponse');
        expect(() => SerializationPal.serializeResponse(undefined, 'foo')).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'brokerResponse');
        expect(() => SerializationPal.serializeResponse(null, null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'brokerResponse');
        expect(() => SerializationPal.serializeResponse(undefined, null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'brokerResponse');
        expect(() => SerializationPal.serializeResponse(null, undefined)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'brokerResponse');
        expect(() => SerializationPal.serializeResponse(undefined, undefined)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'brokerResponse');

        const mockBrokerResponse = new BrokerMessage.Response(null, null);

        expect(() => SerializationPal.serializeResponse(mockBrokerResponse, null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'id');
        expect(() => SerializationPal.serializeResponse(mockBrokerResponse, null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'id');
        expect(() => SerializationPal.serializeResponse(mockBrokerResponse, undefined)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'id');
        expect(() => SerializationPal.serializeResponse(mockBrokerResponse, undefined)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'id');
    });

    test(`serializeResponse doesn't throw for good args`, () => {
        const success = new BrokerMessage.Response(null, null);
        const error = new BrokerMessage.Response(null, new MockError());

        expect(() => SerializationPal.serializeResponse(success, 'foo')).not.toThrow();
        expect(() => SerializationPal.serializeResponse(error, 'foo')).not.toThrow();
    });

    test(`extract throws for falsy args`, () => {
        expect(() => SerializationPal.extract(
            new BrokerMessage.InboundRequest('foo', [], 10),
            null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'defaultTimeout');

        expect(() => SerializationPal.extract(
            null,
            null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'request');
    });

    test(`extract and serializeRequest produce a non-empty buffer`, () => {
        expect(SerializationPal.serializeRequest(
            'foo-id',
            'foo-method',
            [],
            100,
            CancellationToken.none
        )).toBeMatchedBy<Buffer>(x => x && x.length > 0);
    });

    test(`extract detects cancellation tokens`, async () => {
        const token1 = SerializationPal.extract(new BrokerMessage.OutboundRequest('foo', []), TimeSpan.fromMilliseconds(1)).cancellationToken;
        expect(token1).toBeTruthy();

        expect(token1.isCancellationRequested).toBe(false);
        await PromisePal.delay(TimeSpan.fromMilliseconds(10));
        expect(token1.isCancellationRequested).toBe(true);

        const cts2 = new CancellationTokenSource();
        const token2 = SerializationPal.extract(new BrokerMessage.OutboundRequest('foo', [cts2.token]), TimeSpan.fromHours(10)).cancellationToken;
        expect(token2).toBeTruthy();

        expect(token2.isCancellationRequested).toBe(false);
        cts2.cancel();
        expect(token2.isCancellationRequested).toBe(true);

        const cts3 = new CancellationTokenSource();
        const token3 = SerializationPal.extract(new BrokerMessage.OutboundRequest('foo', ['foo2', cts3.token]), TimeSpan.fromHours(10)).cancellationToken;
        expect(token3).toBeTruthy();

        expect(token3.isCancellationRequested).toBe(false);
        cts3.cancel();
        expect(token3.isCancellationRequested).toBe(true);
    });

    test(`extract extracts timeouts from messages while serializing Outbound requests`, () => {
        const defaultTimeout = TimeSpan.fromHours(10);

        expect(SerializationPal.extract(
            new BrokerMessage.OutboundRequest('foo', []),
            defaultTimeout
        ).timeoutSeconds).toBe(defaultTimeout.totalSeconds);

        expect(SerializationPal.extract(
            new BrokerMessage.OutboundRequest('foo', [new Message(undefined, TimeSpan.fromHours(1))]),
            defaultTimeout
        ).timeoutSeconds).toBe(3600);
    });

    test(`deserializeResponse throws for falsy args`, () => {
        expect(() => SerializationPal.deserializeResponse(null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'wireResponse');
    });

    test(`deserializeResponse works with empty result`, () => {
        const wireResponse = SerializationPal.deserializeResponse(new WireMessage.Response('foo', null, null));
        expect(wireResponse.id).toBe('foo');
        expect(wireResponse.brokerResponse.maybeResult).toBe(null);
        expect(wireResponse.brokerResponse.maybeError).toBe(null);
    });

    test(`deserializeResponse works with error result`, () => {
        const wireError = new WireMessage.Error(
            'error-message-1', 'stack', 'error-type-1',
            new WireMessage.Error('error-2', 'stack', 'type', null)
        );
        const wireResponse = SerializationPal.deserializeResponse(new WireMessage.Response('foo', null, wireError));
        expect(wireResponse.id).toBe('foo');
        expect(wireResponse.brokerResponse.maybeResult).toBe(null);
        expect(wireResponse.brokerResponse.maybeError).toBeInstanceOf(Error, x => {
            return x.message === 'error-message-1' && x.name === 'error-type-1';
        });
    });

    test(`deserializeRequest throws for falsy args`, () => {
        expect(() => SerializationPal.deserializeRequest(null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'wireRequest');
    });

    test(`deserializeRequest works`, () => {
        const wireRequest = new WireMessage.Request(
            10,
            'foo',
            'bar',
            ['null', '123', '"test"', '{}']
        );
        const obj = SerializationPal.deserializeRequest(wireRequest);
        expect(obj.id).toBe('foo');
        expect(obj.brokerRequest.methodName).toBe('bar');
        expect(obj.brokerRequest.args).toEqual([null, 123, 'test', {}]);
        expect(obj.brokerRequest).toBeInstanceOf(BrokerMessage.InboundRequest, x => x.timeoutSeconds === 10);
    });

    test(`fromJson throws for generally-falsy args`, () => {
        expect(() => SerializationPal.fromJson('foo', null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'type');
        expect(() => SerializationPal.fromJson('foo', undefined)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'type');

        expect(() => SerializationPal.fromJson('', null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'json');
        expect(() => SerializationPal.fromJson(null, null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'json');
    });

    test(`fromJson throws for invalid WireMessage.Type`, () => {
        expect(() => SerializationPal.fromJson('foo', 139)).toThrowInstanceOf(ArgumentError, x => x.maybeParamName === 'type');
    });

    test(`fromJson doesn't throw when type is WireMessage.Type.Request which is 0 which is falsy`, () => {
        expect(() => SerializationPal.fromJson('{}', WireMessage.Type.Request)).not.toThrow();
    });

    test(`fromJson sets prototypes accord to the specified type`, () => {
        expect(SerializationPal.fromJson('{}', WireMessage.Type.Request) as any).toBeInstanceOf(WireMessage.Request);
        expect(SerializationPal.fromJson('{}', WireMessage.Type.Response) as any).toBeInstanceOf(WireMessage.Response);
    });

    test(`fromJson deserializes WireMessage.Request fields`, () => {
        const original = new WireMessage.Request(100, 'foo', 'bar', ['true', '123', '"test"']);
        const actual = SerializationPal.fromJson(JSON.stringify(original), WireMessage.Type.Request);
        expect(actual).toEqual(original);
    });

    test(`fromJson deserializes WireMessage.Response fields`, () => {
        const original = new WireMessage.Response(
            'foo',
            null,
            new WireMessage.Error(
                'error-message-1',
                'error-stack-1',
                'error-type-1',
                new WireMessage.Error(
                    'error-message-2',
                    'error-stack-2',
                    'error-type-2',
                    null
                )
            )
        );
        const actual = SerializationPal.fromJson(JSON.stringify(original), WireMessage.Type.Response);
        expect(actual).toEqual(original);
    });
});
