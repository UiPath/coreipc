import '../../jest-extensions';
import { MockError, _mock_ } from '../../jest-extensions';

import * as BrokerMessage from '../../../src/core/internals/broker/broker-message';
import * as WireMesssage from '../../../src/core/internals/broker/wire-message';
import { Broker } from '../../../src/core/internals/broker/broker';
import { ArgumentNullError } from '../../../src/foundation/errors/argument-null-error';
import { TimeSpan } from '../../../src/foundation/tasks/timespan';
import { ILogicalSocket } from '../../../src/foundation/pipes/logical-socket';
import { CancellationToken } from '../../../src/foundation/tasks/cancellation-token';
import { IDisposable } from '../../../src/foundation/disposable';
import { SerializationPal } from '../../../src/core/internals/broker/serialization-pal';
import '../../../src/foundation/tasks/promise-pal';

describe('Core-Internals-Broker', () => {
    test(`Broker.ctor throws for falsy args`, () => {
        expect(() => new Broker(null, null, null, null, null, null, null, null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === '_factory');
        expect(() => new Broker(jest.fn(), null, null, null, null, null, null, null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === '_pipeName');
        expect(() => new Broker(jest.fn(), 'foo', null, null, null, null, null, null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === '_connectTimeout');
        expect(() => new Broker(jest.fn(), 'foo', TimeSpan.fromDays(1), null, null, null, null, null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === '_defaultCallTimeout');
        expect(() => new Broker(jest.fn(), 'foo', TimeSpan.fromDays(1), TimeSpan.fromDays(1), null, null, null, null)).not.toThrow();
    });

    test(`Broker.sendReceiveAsync throws for falsy args`, async () => {
        const broker = new Broker(jest.fn(), 'foo', TimeSpan.fromSeconds(1), TimeSpan.fromSeconds(1), null, null, null, null);
        await expect(broker.sendReceiveAsync(null)).rejects.toBeInstanceOf(ArgumentNullError, x => x.maybeParamName === 'brokerRequest');
    });

    test(`Broker.sendReceiveAsync causes connection`, async () => {
        const logicalSocket = _mock_<ILogicalSocket>({
            connectAsync: jest.fn(),
            addDataListener: jest.fn(),
            addEndListener: jest.fn(),
            writeAsync: jest.fn(() => Promise.completedPromise),
            dispose: () => { }
        });
        const logicalSocketFactory = jest.fn(() => logicalSocket);

        const broker = new Broker(logicalSocketFactory, 'foo', TimeSpan.fromSeconds(1), TimeSpan.fromSeconds(1), null, null, null, null);

        broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('bar', []));
        await Promise.yield();

        expect(logicalSocketFactory).toHaveBeenCalledTimes(1);
        expect(logicalSocket.addDataListener).toHaveBeenCalledTimes(1);
        expect(logicalSocket.connectAsync).toHaveBeenCalledTimes(1);

        broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('bar', []));
        await Promise.yield();

        expect(logicalSocketFactory).toHaveBeenCalledTimes(1);
        expect(logicalSocket.addDataListener).toHaveBeenCalledTimes(1);
        expect(logicalSocket.connectAsync).toHaveBeenCalledTimes(1);
    });

    test(`Broker reconnects internally`, async () => {
        let currentSocketIndex = -1;
        const writePromises = [
            Promise.fromError<void>(new MockError()),
            Promise.fromError<void>(new MockError()),
            Promise.completedPromise
        ];
        const logicalSockets = writePromises.map(writePromise => _mock_<ILogicalSocket>({
            connectAsync: jest.fn(() => Promise.completedPromise),
            addDataListener: jest.fn(() => ({ dispose: () => { } })),
            addEndListener: jest.fn(() => ({ dispose: () => { } })),
            writeAsync: jest.fn(() => writePromise),
            dispose: () => { }
        }));
        const logicalSocketFactory = jest.fn(() => logicalSockets[++currentSocketIndex]);

        const broker = new Broker(logicalSocketFactory, 'foo', TimeSpan.fromSeconds(1), TimeSpan.fromSeconds(1), null, null, null, null);

        broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('bar', []));
        await Promise.yield();

        expect(logicalSocketFactory).toHaveBeenCalledTimes(3);
    });

    test(`Broker connects successfully, fails to send, tries to reconnect and dies`, async () => {
        let currentSocketIndex = -1;
        const connectError = new MockError();
        const promisePairs = [
            {
                connect: Promise.completedPromise,
                write: Promise.fromError<void>(new MockError())
            },
            {
                connect: Promise.completedPromise,
                write: Promise.fromError<void>(new MockError())
            },
            {
                connect: Promise.fromError(connectError),
                write: Promise.completedPromise
            },
        ];
        const logicalSockets = promisePairs.map(pair => _mock_<ILogicalSocket>({
            connectAsync: jest.fn(() => pair.connect),
            addDataListener: jest.fn(() => ({ dispose: () => { } })),
            addEndListener: jest.fn(() => ({ dispose: () => { } })),
            writeAsync: jest.fn(() => pair.write),
            dispose: () => { }
        }));
        const logicalSocketFactory = jest.fn(() => logicalSockets[++currentSocketIndex]);

        const broker = new Broker(logicalSocketFactory, 'foo', TimeSpan.fromSeconds(1), TimeSpan.fromSeconds(1), null, null, null, null);

        await expect(broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('bar', []))).rejects.toBe(connectError);
    });

    test(`Broker.sendReceiveAsync works`, async () => {
        class MockLogicalSocket implements ILogicalSocket {
            private readonly _listeners = new Array<(data: Buffer) => void>();

            constructor(private readonly _response: BrokerMessage.Response) { }

            public async connectAsync(path: string, maybeTimeout: TimeSpan, cancellationToken: CancellationToken): Promise<void> { }
            public async writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
                expect(this._listeners.length).toBe(1);

                await Promise.yield();

                const requestJson = buffer.toString('utf8', 5);
                const wireRequest = SerializationPal.fromJson(requestJson, WireMesssage.Type.Request);
                const requestId = wireRequest.Id;
                const responseBuffer = SerializationPal.serializeResponse(this._response, requestId);
                this._listeners[0](responseBuffer);
            }
            public addDataListener(listener: (data: Buffer) => void): IDisposable {
                expect(this._listeners.length).toBe(0);
                this._listeners.push(listener);
                return {
                    dispose: () => {
                        const index = this._listeners.indexOf(listener);
                        if (index >= 0) {
                            this._listeners.splice(index, 1);
                        }
                    }
                };
            }
            public addEndListener(listener: () => void): IDisposable {
                return { dispose: () => { } };
            }

            public dispose(): void { }
        }

        const expectedResponse = new BrokerMessage.Response(123, null);
        const broker = new Broker(() => new MockLogicalSocket(expectedResponse), 'foo', TimeSpan.fromSeconds(1), TimeSpan.fromSeconds(1), null, null, null, null);
        const request = new BrokerMessage.OutboundRequest('bar', []);
        await expect(broker.sendReceiveAsync(request)).resolves.toEqual(expectedResponse);
        try {
            await broker.sendReceiveAsync(request);
        } catch (error) {
            console.error(error);
        }
        await expect(broker.sendReceiveAsync(request)).resolves.toEqual(expectedResponse);
    });

    test(`Broker issues callback`, async () => {
        let listener: (data: Buffer) => void | null = null;
        const logicalSocket = _mock_<ILogicalSocket>({
            connectAsync: jest.fn(() => Promise.completedPromise),
            addDataListener: jest.fn((x: (data: Buffer) => void) => {
                listener = x;
                return { dispose: () => { } };
            }),
            addEndListener: jest.fn(() => ({ dispose: () => { } })),
            writeAsync: jest.fn(() => Promise.completedPromise),
            dispose: () => { }
        });

        const callbacks = {
            bar: jest.fn(() => Promise.fromResult(200))
        };

        const broker = new Broker(() => logicalSocket, 'foo', TimeSpan.fromSeconds(1), TimeSpan.fromSeconds(1), callbacks, null, null, null);

        broker.sendReceiveAsync(new BrokerMessage.OutboundRequest('remote-method', []));
        await Promise.yield();

        expect(logicalSocket.addDataListener).toHaveBeenCalledTimes(1);
        expect(listener).toBeTruthy();

        const brokerRequest = new BrokerMessage.InboundRequest('bar', ['frob', 123], 1);
        const tuple = SerializationPal.extract(brokerRequest, TimeSpan.fromHours(1));
        const callbackRequestBuffer = SerializationPal.serializeRequest('some-id', brokerRequest.methodName, tuple.serializedArgs, tuple.timeoutSeconds, tuple.cancellationToken);
        expect(callbacks.bar).not.toHaveBeenCalled();
        listener(callbackRequestBuffer);
        await Promise.yield();
        expect(callbacks.bar).toHaveBeenCalledTimes(1);
        expect(callbacks.bar).toHaveBeenCalledWith('frob', 123);
        expect(logicalSocket.writeAsync).toHaveBeenCalledTimes(2);
    });

    test(`Broker.disposeAsync works and is idempotent`, async () => {
        const broker = new Broker(jest.fn(), 'foo', TimeSpan.fromDays(1), TimeSpan.fromDays(1), null, null, null, null);

        await expect(broker.disposeAsync()).resolves.toBeUndefined();
        await expect(broker.disposeAsync()).resolves.toBeUndefined();
    });
});
