import '../jest-extensions';
import { _mock_ } from '../jest-extensions';
import { PipeReader } from '../../src/foundation/pipes/pipe-reader';
import { ArgumentNullError } from '../../src/foundation/errors/argument-null-error';
import { ILogicalSocket } from '../../src/foundation/pipes/logical-socket';
import { IDisposable } from '../../src/foundation/disposable/disposable';
import { CancellationToken, CancellationTokenSource, PromiseCompletionSource, PromisePal } from '../../src';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';
import { ObjectDisposedError } from '../../src/foundation/errors/object-disposed-error';
import { InvalidOperationError } from '../../src/foundation/errors/invalid-operation-error';

describe('Foundation-PipeReader', () => {

    test(`ctor works`, () => {
        expect(() => new PipeReader(null as any)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === '_socket');

        const mock = _mock_<ILogicalSocket>({
            addDataListener: jest.fn()
        });
        expect(() => new PipeReader(mock)).not.toThrow();

        expect(mock.addDataListener).toHaveBeenCalledTimes(1);
    });

    test(`disposeAsync works`, async () => {
        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({ addDataListener: jest.fn(() => _disposable) });

        const reader = new PipeReader(_logicalSocket);
        expect(_logicalSocket.addDataListener).toHaveBeenCalledTimes(1);
        expect(_disposable.dispose).not.toHaveBeenCalled();

        await expect(reader.disposeAsync()).resolves.toBeUndefined();
        expect(_disposable.dispose).toHaveBeenCalledTimes(1);

        await expect(reader.disposeAsync()).resolves.toBeUndefined();
        expect(_disposable.dispose).toHaveBeenCalledTimes(1);
    });

    test(`readPartiallyAsync succeeds even with immediate cancellation when data is already available`, async () => {
        const alreadyAvailable = Buffer.from('test-message');

        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({
            addDataListener: jest.fn((listener: (data: Buffer) => void) => {
                listener(alreadyAvailable);
                return _disposable;
            })
        });

        const actual = Buffer.alloc(alreadyAvailable.length);
        const reader = new PipeReader(_logicalSocket);
        const cts = new CancellationTokenSource(); cts.cancel();
        await expect(reader.readPartiallyAsync(actual, cts.token)).resolves.toBe(actual.length);
        expect(actual).toEqual(alreadyAvailable);
    });

    test(`readPartiallyAsync succeeds even with immediate cancellation even when data is only partially available`, async () => {
        const alreadyAvailable = Buffer.from('test');

        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({
            addDataListener: jest.fn((listener: (data: Buffer) => void) => {
                listener(alreadyAvailable);
                return _disposable;
            })
        });

        const actual = Buffer.alloc(alreadyAvailable.length * 2);
        const reader = new PipeReader(_logicalSocket);
        const cts = new CancellationTokenSource(); cts.cancel();
        await expect(reader.readPartiallyAsync(actual, cts.token)).resolves.toBe(alreadyAvailable.length);
        expect(actual.subarray(0, alreadyAvailable.length)).toEqual(alreadyAvailable);
    });

    test(`readPartiallyAsync succeeds even with immediate cancellation even when no data is available if the destination is zero length`, async () => {
        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({
            addDataListener: jest.fn((listener: (data: Buffer) => void) => {
                return _disposable;
            })
        });

        const actual = Buffer.alloc(0);
        const reader = new PipeReader(_logicalSocket);
        const cts = new CancellationTokenSource(); cts.cancel();
        await expect(reader.readPartiallyAsync(actual, cts.token)).resolves.toBe(0);
    });

    test(`readPartiallyAsync fails as canceled with immediate cancellation when no data is available and destination is not zero length`, async () => {
        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({ addDataListener: jest.fn((listener: (data: Buffer) => void) => _disposable) });

        const actual = Buffer.alloc(10);
        const reader = new PipeReader(_logicalSocket);
        const cts = new CancellationTokenSource(); cts.cancel();
        await expect(reader.readPartiallyAsync(actual, cts.token)).rejects.toBeInstanceOf(OperationCanceledError);
    });

    test(`readPartiallyAsync fails for falsy args`, async () => {
        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({ addDataListener: jest.fn((listener: (data: Buffer) => void) => _disposable) });

        const reader = new PipeReader(_logicalSocket);
        expect(reader.readPartiallyAsync(null as any, CancellationToken.none)).rejects.toBeInstanceOf(ArgumentNullError, error => error.maybeParamName === 'destination');
        expect(reader.readPartiallyAsync(null as any, null as any)).rejects.toBeInstanceOf(ArgumentNullError, error => error.maybeParamName === 'destination');
        expect(reader.readPartiallyAsync(Buffer.alloc(0), null as any)).rejects.toBeInstanceOf(ArgumentNullError, error => error.maybeParamName === 'cancellationToken');
    });

    test(`readPartiallyAsync fails when reader is disposed`, async () => {
        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({ addDataListener: jest.fn((listener: (data: Buffer) => void) => _disposable) });

        const reader = new PipeReader(_logicalSocket);
        await reader.disposeAsync();

        await expect(reader.readPartiallyAsync(Buffer.alloc(0), CancellationToken.none)).rejects.toBeInstanceOf(ObjectDisposedError);
    });

    test(`readPartiallyAsync fails when called twice concurrently`, async () => {
        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({ addDataListener: jest.fn((listener: (data: Buffer) => void) => _disposable) });

        const reader = new PipeReader(_logicalSocket);
        const promise1 = reader.readPartiallyAsync(Buffer.alloc(10), CancellationToken.none);
        await expect(reader.readPartiallyAsync(Buffer.alloc(10), CancellationToken.none)).rejects.toBeInstanceOf(InvalidOperationError);
    });

    test(`readPartiallyAsync works well when there's more than enough available data`, async () => {
        const expectedComponents = [
            'strawberry', 'cherry', 'raspberry'
        ].map(str => Buffer.from(str));

        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({
            addDataListener: jest.fn((listener: (data: Buffer) => void) => {
                listener(Buffer.from('straw'));
                listener(Buffer.from('berrycherryrasp'));
                listener(Buffer.from('berryblack'));
                return _disposable;
            })
        });

        const reader = new PipeReader(_logicalSocket);
        for (const expectedComponent of expectedComponents) {
            const actualComponent = Buffer.alloc(expectedComponent.length);
            await expect(reader.readPartiallyAsync(actualComponent, CancellationToken.none)).resolves.toBe(expectedComponent.length);
            expect(actualComponent).toEqual(expectedComponent);
        }
        {
            const actualComponent = Buffer.alloc('blackberry'.length);
            const expectedComponent = Buffer.from('black');
            await expect(reader.readPartiallyAsync(actualComponent, CancellationToken.none)).resolves.toBe(expectedComponent.length);
            expect(actualComponent.subarray(0, expectedComponent.length)).toEqual(expectedComponent);
        }
    });

    test(`readPartiallyAsync works when data delivery is deferred`, async () => {
        const components = [
            'strawberry',
            'cherry',
            'raspberry',
            'blackberry'
        ].map(str => ({
            str,
            trigger: new PromiseCompletionSource<void>()
        }));

        const _disposable = _mock_<IDisposable>({ dispose: jest.fn() });
        const _logicalSocket = _mock_<ILogicalSocket>({
            addDataListener: jest.fn((listener: (data: Buffer) => void) => {
                (async () => {
                    for (const component of components) {
                        await component.trigger.promise;
                        listener(Buffer.from(component.str));
                    }
                })();
                return _disposable;
            })
        });

        const reader = new PipeReader(_logicalSocket);
        const buffer = Buffer.alloc(30);

        for (const component of components) {
            const promise = reader.readPartiallyAsync(buffer, CancellationToken.none);
            const _then = jest.fn(); const _catch = jest.fn();
            promise.then(_then, _catch);

            await PromisePal.yield();
            expect(_then).not.toHaveBeenCalled();
            expect(_catch).not.toHaveBeenCalled();

            component.trigger.setResult(undefined);
            await PromisePal.yield();

            expect(_then).toHaveBeenCalledTimes(1);
            expect(_then).toHaveBeenCalledWith(Buffer.byteLength(component.str));
            expect(buffer.subarray(0, Buffer.byteLength(component.str))).toEqual(Buffer.from(component.str));
        }
    });

});
