import '../jest-extensions';
import { PipeClientStream } from '../../src/foundation/pipes/pipe-client-stream';
import { ILogicalSocket } from '../../src/foundation/pipes/logical-socket';
import { TimeSpan } from '../../src/foundation/tasks/timespan';
import { CancellationToken } from '../../src/foundation/tasks/cancellation-token';
import { IDisposable } from '../../src/foundation/disposable/disposable';
import { PromiseCompletionSource, PromisePal } from '../../src';
import { ObjectDisposedError } from '../../src/foundation/errors/object-disposed-error';

describe('Foundation-PipeClientStream', () => {
    class MockLogicalSocket implements ILogicalSocket {
        public connectAsync: (path: string, maybeTimeout: TimeSpan | null, cancellationToken: CancellationToken) => Promise<void> = jest.fn();
        public writeAsync: (buffer: Buffer, cancellationToken: CancellationToken) => Promise<void> = jest.fn();
        public addDataListener: (listener: (data: Buffer) => void) => IDisposable = jest.fn();
        public addEndListener: (listener: () => void) => IDisposable = jest.fn();
        public dispose: () => void = jest.fn();
    }

    test(`ctor calls ILogicalSocket.connectAsync`, () => {
        const socket = new MockLogicalSocket();

        const methods = [socket.connectAsync, socket.addDataListener, socket.dispose, socket.writeAsync];
        for (const method of methods) {
            expect(method).not.toHaveBeenCalled();
        }

        let promise: Promise<PipeClientStream> | null;
        expect(() => promise = PipeClientStream.connectAsync(() => socket, 'foo', null)).not.toThrow();
        expect(promise).toBeTruthy();

        expect(socket.connectAsync).toHaveBeenCalledTimes(1);
        expect(socket.connectAsync).toHaveBeenCalledWith('\\\\.\\pipe\\foo', null as TimeSpan | null, CancellationToken.none);

        for (const method of methods) {
            if (method !== socket.connectAsync) {
                expect(method).not.toHaveBeenCalled();
            }
        }
    });

    test(`PipeClientStream lifecycle`, async () => {
        const pcs = new PromiseCompletionSource<void>();
        const socket = new MockLogicalSocket();
        socket.connectAsync = jest.fn((path: string, maybeTimeout: TimeSpan, cancellationToken: CancellationToken): Promise<void> => {
            return pcs.promise;
        });
        socket.addDataListener = jest.fn(() => ({ dispose: () => { } }));
        socket.addEndListener = jest.fn(() => ({ dispose: () => { } }));

        const methods = [socket.connectAsync, socket.addDataListener, socket.dispose, socket.writeAsync];
        for (const method of methods) {
            expect(method).not.toHaveBeenCalled();
        }

        let promise: Promise<PipeClientStream> | null;
        expect(() => promise = PipeClientStream.connectAsync(() => socket, 'foo', null)).not.toThrow();
        expect(promise).toBeTruthy();

        expect(socket.connectAsync).toHaveBeenCalledTimes(1);
        expect(socket.connectAsync).toHaveBeenCalledWith('\\\\.\\pipe\\foo', null as TimeSpan | null, CancellationToken.none);

        for (const method of methods) {
            if (method !== socket.connectAsync) {
                expect(method).not.toHaveBeenCalled();
            }
        }

        const _then = jest.fn();
        const _catch = jest.fn();
        promise.then(_then, _catch);

        expect(_then).not.toHaveBeenCalled();
        pcs.setResult(undefined);
        expect(_then).not.toHaveBeenCalled();
        await PromisePal.yield();
        expect(_then).toHaveBeenCalled();

        let stream: PipeClientStream | null = null;
        await expect(promise).resolves.toBeMatchedBy<PipeClientStream>(result => {
            stream = result;
            return result instanceof PipeClientStream;
        });

        expect(socket.dispose).not.toHaveBeenCalled();

        await expect(stream.disposeAsync()).resolves.toBeUndefined();
        expect(socket.dispose).toHaveBeenCalledTimes(1);
    });

    test(`PipeClientStream.writeAsync works`, async () => {
        const socket = new MockLogicalSocket();
        socket.connectAsync = jest.fn((path: string, maybeTimeout: TimeSpan, cancellationToken: CancellationToken): Promise<void> => {
            return PromisePal.completedPromise;
        });
        socket.addDataListener = jest.fn(() => ({ dispose: () => { } }));
        socket.addEndListener = jest.fn(() => ({ dispose: () => { } }));
        socket.writeAsync = jest.fn((buffer: Buffer, cancellationToken: CancellationToken) => {
            return PromisePal.completedPromise;
        });

        const mockBuffer0 = Buffer.alloc(0);
        const stream = await PipeClientStream.connectAsync(() => socket, 'foo', null);
        await expect(stream.writeAsync(mockBuffer0)).resolves.toBeUndefined();

        expect(socket.writeAsync).not.toHaveBeenCalled();

        const mockBuffer10 = Buffer.alloc(10);
        await expect(stream.writeAsync(mockBuffer10)).resolves.toBeUndefined();

        expect(socket.writeAsync).toHaveBeenCalledTimes(1);
        expect(socket.writeAsync).toHaveBeenCalledWith(mockBuffer10, CancellationToken.none);

        await expect(stream.readAsync(mockBuffer0)).resolves.toBeUndefined();

        await expect(stream.disposeAsync()).resolves.toBeUndefined();
        await expect(stream.disposeAsync()).resolves.toBeUndefined();
        await expect(stream.writeAsync(mockBuffer10)).rejects.toBeInstanceOf(ObjectDisposedError);

        await expect(stream.readPartiallyAsync(mockBuffer10)).rejects.toBeInstanceOf(ObjectDisposedError);
        await expect(stream.readAsync(mockBuffer10)).rejects.toBeInstanceOf(ObjectDisposedError);
    });
});
