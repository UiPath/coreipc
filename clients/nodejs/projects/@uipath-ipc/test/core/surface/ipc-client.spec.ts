import { IpcClient, InternalIpcClientConfig } from '../../../src/core/surface/ipc-client';
import { Maybe } from '../../../src/foundation/data-structures/maybe';
import { MockError } from '../../jest-extensions';
import { ArgumentNullError } from '../../../src/foundation/errors/argument-null-error';
import { CancellationToken } from '../../../src/foundation/tasks/cancellation-token';
import { ILogicalSocket } from '../../../src/foundation/pipes/logical-socket';
import { IDisposable } from '../../../src/foundation/disposable';
import { TimeSpan } from '../../../src/foundation/tasks/timespan';
import { __returns__ } from '../../../src/core/surface/rtti';
import '../../../src/foundation/tasks/promise-pal';

describe('Core-Surface-IpcClient', () => {

    class Integer {
        constructor(public readonly value: number = 0) { }
    }
    class IService {
        @__returns__(Integer)
        // @ts-ignore
        public addAsync(x: Integer, y: Integer): Promise<Integer> { throw null; }
    }
    class MockLogicalSocket implements ILogicalSocket {
        public static _connectAsync: (path: string, maybeTimeout: TimeSpan, cancellationToken: CancellationToken) => Promise<void>;
        public static _writeAsync: (buffer: Buffer, cancellationToken: CancellationToken) => Promise<void>;
        public static _addDataListener: (listener: (data: Buffer) => void) => IDisposable;
        public static _addEndListener: (listener: () => void) => IDisposable;
        public static _dispose: () => void;

        public connectAsync(path: string, maybeTimeout: TimeSpan, cancellationToken: CancellationToken): Promise<void> {
            return MockLogicalSocket._connectAsync(path, maybeTimeout, cancellationToken);
        }
        public writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
            return MockLogicalSocket._writeAsync(buffer, cancellationToken);
        }
        public addDataListener(listener: (data: Buffer) => void): IDisposable {
            return MockLogicalSocket._addDataListener(listener);
        }
        public addEndListener(listener: () => void): IDisposable {
            return MockLogicalSocket._addEndListener(listener);
        }
        public dispose(): void {
            MockLogicalSocket._dispose();
        }
    }

    test(`ctor doesn't throw when it shouldn't`, async () => {
        const cases: Array<() => IpcClient<unknown>> = [
            () => new IpcClient('foo', Object),
            () => new IpcClient('foo', Object, config => { })
        ];
        for (const _case of cases) {
            let instance: Maybe<IpcClient<unknown>>;
            expect(() => instance = _case()).not.toThrow();
            try { await instance.closeAsync(); } catch (error) { }
        }
    });

    test(`ctor throws when it should`, async () => {
        const mockError = new MockError();
        const cases: Array<{
            factory: () => IpcClient<unknown>;
            validator: (matchers: jest.Matchers<any>) => void;
        }> = [
                {
                    factory: () => new IpcClient(null as any, Object),
                    validator: matchers => matchers.toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'pipeName')
                },
                {
                    factory: () => new IpcClient('foo', null),
                    validator: matchers => matchers.toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'serviceCtor')
                },
                {
                    factory: () => new IpcClient(null as any, null),
                    validator: matchers => matchers.toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'pipeName')
                },
                {
                    factory: () => new IpcClient('foo', Object, config => { throw mockError; }),
                    validator: matchers => matchers.toThrowInstance(mockError)
                }
            ];
        for (const _case of cases) {
            let instance: Maybe<IpcClient<unknown>>;
            _case.validator(expect(() => instance = _case.factory()));
            try { await instance.closeAsync(); } catch (error) { }
        }
    });

    test('IpcClient works 1', async () => {
        MockLogicalSocket._connectAsync = jest.fn(() => Promise.completedPromise);
        MockLogicalSocket._addDataListener = jest.fn();
        MockLogicalSocket._writeAsync = jest.fn();
        MockLogicalSocket._dispose = jest.fn();

        const client = new IpcClient(
            'foo',
            IService,
            (config: InternalIpcClientConfig<IService>) => {
                config.logicalSocketFactory = () => new MockLogicalSocket();
                config.defaultCallTimeoutSeconds = 60 * 1000;
            }
        );

        expect(client.proxy).toBeTruthy();
        expect(client.proxy.addAsync).toBeInstanceOf(Function);

        expect(MockLogicalSocket._connectAsync).not.toHaveBeenCalled();
        // const result = await client.proxy.addAsync(new Integer(), new Integer());
        // expect(MockLogicalSocket._connectAsync).toHaveBeenCalled();

        await client.closeAsync();
    });

});
