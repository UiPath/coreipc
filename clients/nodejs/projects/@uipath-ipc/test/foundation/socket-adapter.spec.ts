import '../jest-extensions';
import { ISocketLike } from '../../src/foundation/pipes/socket-like';
import { SocketAdapter } from '../../src/foundation/pipes/socket-adapter';
import { ArgumentNullError } from '../../src/foundation/errors/argument-null-error';
import { CancellationToken, CancellationTokenSource, PromiseCompletionSource } from '../../src';
import { ObjectDisposedError } from '../../src/foundation/errors/object-disposed-error';
import { InvalidOperationError } from '../../src/foundation/errors/invalid-operation-error';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';
import { TimeSpan } from '../../src/foundation/tasks/timespan';
import { TimeoutError } from '../../src/foundation/errors/timeout-error';
import { _mock_, MockError } from '../jest-extensions';
import { PipeBrokenError } from '../../src/foundation/errors/pipe/pipe-broken-error';
import '../../src/foundation/tasks/promise-pal';

class MockSocketLikeBase implements ISocketLike {
    constructor(callback?: (instance: MockSocketLikeBase) => void) {
        if (callback) {
            callback(this);
        }
    }
    public makeAutoConnectable(): void {
        this.connect = (path: string, connectionListener?: () => void) => {
            connectionListener();
            return this;
        };
    }

    public connect: (path: string, connectionListener?: () => void) => this = jest.fn();
    public once: (path: string, listener: (err: Error) => void) => this = jest.fn();
    public write: (buffer: string | Uint8Array, cb?: (err?: Error) => void) => boolean = jest.fn();
    public addListener: (event: 'data' | 'end', listener: (data?: Buffer) => void) => this = jest.fn();
    public removeListener: (event: 'data' | 'end', listener: (data?: Buffer) => void) => this = jest.fn();
    public removeAllListeners: (event?: string | symbol) => this = jest.fn();
    public unref: () => void = jest.fn();
    public destroy: (error?: Error) => void = jest.fn();
}

describe('Foundation-SocketAdapter', () => {

    test(`ctor doesn't throw for valid args`, () => {
        expect(() => new SocketAdapter(new MockSocketLikeBase())).not.toThrow();
    });
    test(`ctor throws for falsy args`, () => {
        expect(() => new SocketAdapter(null as any)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === '_socketLike');
    });

    test(`connectAsync throws for falsy args`, async () => {
        const cases: Array<{
            path: string | null | undefined;
            ct: CancellationToken | null | undefined,
            expectedParamName: string
        }> = [
                { path: '', ct: CancellationToken.none, expectedParamName: 'path' },
                { path: null, ct: CancellationToken.none, expectedParamName: 'path' },
                { path: undefined, ct: CancellationToken.none, expectedParamName: 'path' },

                { path: 'foo', ct: null, expectedParamName: 'cancellationToken' },
                { path: '', ct: null, expectedParamName: 'path' },
                { path: null, ct: null, expectedParamName: 'path' },
                { path: undefined, ct: null, expectedParamName: 'path' },

                { path: 'foo', ct: undefined, expectedParamName: 'cancellationToken' },
                { path: '', ct: undefined, expectedParamName: 'path' },
                { path: null, ct: undefined, expectedParamName: 'path' },
                { path: undefined, ct: undefined, expectedParamName: 'path' },
            ];
        const adapter = new SocketAdapter(new MockSocketLikeBase());

        for (const _case of cases) {
            await expect(adapter.connectAsync(_case.path, null, _case.ct)).rejects.toBeInstanceOf(ArgumentNullError, error => error.maybeParamName === _case.expectedParamName);
        }
    });

    test(`connectAsync throws when adapter is disposed`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        adapter.dispose();
        await expect(adapter.connectAsync('somewhere', null, CancellationToken.none)).rejects.toBeInstanceOf(ObjectDisposedError);
    });

    test(`connectAsync throws when adapter is disposed`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        adapter.dispose();
        await expect(adapter.connectAsync('somewhere', null, CancellationToken.none)).rejects.toBeInstanceOf(ObjectDisposedError);
    });

    test(`connectAsync throws when already issued a call to connectAsync`, async () => {
        const socketLike = new MockSocketLikeBase();
        socketLike.connect = (path, connectionListener) => {
            connectionListener();
            return socketLike;
        };
        const adapter = new SocketAdapter(socketLike);

        const promise1 = adapter.connectAsync('somewhere', null, CancellationToken.none);
        await expect(promise1).resolves.toBeUndefined();

        const promise2 = adapter.connectAsync('anywhere', null, CancellationToken.none);
        await expect(promise2).rejects.toBeInstanceOf(InvalidOperationError);
    });

    test(`connectAsync can be cancelled`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        const cts = new CancellationTokenSource();
        const promise = adapter.connectAsync('somewhere', null, cts.token);

        cts.cancel();
        await expect(promise).rejects.toBeInstanceOf(OperationCanceledError);
    });

    test(`connectAsync times out as expected`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        const promise = adapter.connectAsync('somewhere', TimeSpan.fromMilliseconds(1), CancellationToken.none);
        await expect(promise).rejects.toBeInstanceOf(TimeoutError);
    }, 100);

    test(`connectAsync: race between ct and timeout won by ct`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        const cts = new CancellationTokenSource();
        cts.cancelAfter(TimeSpan.fromMilliseconds(5));

        const promise = adapter.connectAsync('somewhere', TimeSpan.fromMilliseconds(10), cts.token);
        await expect(promise).rejects.toBeInstanceOf(OperationCanceledError);
    });

    test(`connectAsync: race between ct and timeout won by timeout`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        const cts = new CancellationTokenSource();
        cts.cancelAfter(TimeSpan.fromMilliseconds(10));

        const promise = adapter.connectAsync('somewhere', TimeSpan.fromMilliseconds(5), cts.token);
        await expect(promise).rejects.toBeInstanceOf(TimeoutError);
    });

    test(`connectAsync throws PipeBrokenError when it receives 'EPIPE' from the underlying ISocketLike`, async () => {
        const trigger = new PromiseCompletionSource<void>();
        const socketLike = _mock_<ISocketLike>({
            connect: jest.fn(),
            once: (event: 'error', listener: (err: Error) => void) => {
                (async () => {
                    await trigger.promise;
                    const err = new Error() as NodeJS.ErrnoException;
                    err.code = 'EPIPE';
                    listener(err);
                })();
                return socketLike;
            }
        });
        const adapter = new SocketAdapter(socketLike);
        const promise = adapter.connectAsync('foo', null, CancellationToken.none);
        const _then = jest.fn();
        const _catch = jest.fn();
        promise.then(_then, _catch);

        trigger.setResult(undefined);
        await Promise.yield();

        expect(_then).not.toHaveBeenCalled();
        expect(_catch).toHaveBeenCalledTimes(1);
        expect(_catch).toHaveBeenCalledWith(new PipeBrokenError());

        await expect(promise).rejects.toBeInstanceOf(PipeBrokenError);
    });

    test(`writeAsync throws for falsy args`, async () => {
        const notNull = Buffer.alloc(10);

        const cases: Array<{
            buffer: Buffer | null | undefined;
            ct: CancellationToken | null | undefined,
            expectedParamName: string
        }> = [
                { buffer: null, ct: CancellationToken.none, expectedParamName: 'buffer' },
                { buffer: undefined, ct: CancellationToken.none, expectedParamName: 'buffer' },
                { buffer: null, ct: null, expectedParamName: 'buffer' },
                { buffer: undefined, ct: null, expectedParamName: 'buffer' },
                { buffer: null, ct: undefined, expectedParamName: 'buffer' },
                { buffer: undefined, ct: undefined, expectedParamName: 'buffer' },
                { buffer: notNull, ct: null, expectedParamName: 'cancellationToken' },
                { buffer: notNull, ct: undefined, expectedParamName: 'cancellationToken' }
            ];
        const adapter = new SocketAdapter(new MockSocketLikeBase());

        for (const _case of cases) {
            await expect(adapter.writeAsync(_case.buffer, _case.ct)).rejects.toBeInstanceOf(ArgumentNullError, error => error.maybeParamName === _case.expectedParamName);
        }
    });

    test(`writeAsync fails if adapter is disposed`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        adapter.dispose();
        await expect(adapter.writeAsync(Buffer.from('foo'), CancellationToken.none)).rejects.toBeInstanceOf(ObjectDisposedError);
    });

    test(`writeAsync fails if adapter is not connected`, async () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        await expect(adapter.writeAsync(Buffer.from('foo'), CancellationToken.none)).rejects.toBeInstanceOf(InvalidOperationError);
    });

    test(`writeAsync resolves when the underlying ISocketLike calls back successfully`, async () => {
        const trigger1 = new PromiseCompletionSource<void>();
        const trigger2 = new PromiseCompletionSource<void>();

        const socketLike = new MockSocketLikeBase();
        socketLike.connect = (path: string, connectionListener?: () => void) => {
            (async () => {
                await trigger1.promise;
                connectionListener();
            })();
            return socketLike;
        };
        socketLike.write = (buffer: string | Uint8Array, cb?: (err?: Error) => void) => {
            (async () => {
                await trigger2.promise;
                cb();
            })();
            return true;
        };
        const adapter = new SocketAdapter(socketLike);

        const promiseConnect = adapter.connectAsync('foo', null, CancellationToken.none);

        trigger1.setResult(undefined);
        await Promise.yield();

        await expect(promiseConnect).resolves.toBeUndefined();

        const promiseWrite = adapter.writeAsync(Buffer.from('foo'), CancellationToken.none);
        const _then = jest.fn();
        const _catch = jest.fn();
        promiseWrite.then(_then, _catch);

        trigger2.setResult(undefined);
        await Promise.yield();

        expect(_then).toHaveBeenCalledTimes(1);
        await expect(promiseWrite).resolves.toBeUndefined();
    });

    test(`writeAsync rejects when the underlying ISocketLike calls back erronously`, async () => {
        const mockError = new MockError();
        const trigger1 = new PromiseCompletionSource<void>();
        const trigger2 = new PromiseCompletionSource<void>();

        const socketLike = new MockSocketLikeBase();
        socketLike.connect = (path: string, connectionListener?: () => void) => {
            (async () => {
                await trigger1.promise;
                connectionListener();
            })();
            return socketLike;
        };
        socketLike.write = (buffer: string | Uint8Array, cb?: (err?: Error) => void) => {
            (async () => {
                await trigger2.promise;
                cb(mockError);
            })();
            return true;
        };
        const adapter = new SocketAdapter(socketLike);

        const promiseConnect = adapter.connectAsync('foo', null, CancellationToken.none);

        trigger1.setResult(undefined);
        await Promise.yield();

        await expect(promiseConnect).resolves.toBeUndefined();

        const promiseWrite = adapter.writeAsync(Buffer.from('foo'), CancellationToken.none);
        const _then = jest.fn();
        const _catch = jest.fn();
        promiseWrite.then(_then, _catch);

        trigger2.setResult(undefined);
        await Promise.yield();

        expect(_catch).toHaveBeenCalledTimes(1);
        await expect(promiseWrite).rejects.toBe(mockError);
    });

    test(`addDataListener throws for falsy args`, () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());

        expect(() => adapter.addDataListener(null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'listener');
        expect(() => adapter.addDataListener(undefined)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'listener');
    });

    test(`addDataListener fails if adapter is disposed`, () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        adapter.dispose();
        expect(() => adapter.addDataListener(_ => { })).toThrowInstanceOf(ObjectDisposedError);
    });

    test(`addDataListener fails if adapter is not connected`, () => {
        const adapter = new SocketAdapter(new MockSocketLikeBase());
        expect(() => adapter.addDataListener(_ => { })).toThrowInstanceOf(InvalidOperationError);
    });

    test(`addDataListener succeeds when it should`, async () => {
        const socketLike = new MockSocketLikeBase();
        socketLike.connect = (path: string, connectionListener?: () => void) => {
            connectionListener();
            return socketLike;
        };
        socketLike.addListener = (event: 'data', listener: (data: Buffer) => void) => {
            (async () => {
                // await trigger.promise;
                listener(Buffer.from('foo'));
            })();
            return socketLike;
        };
        const adapter = new SocketAdapter(socketLike);
        await adapter.connectAsync('foo', null, CancellationToken.none);

        expect(() => adapter.addDataListener(_ => { })).not.toThrow();
    });

    test(`dispose doesn't throw and is idempotent`, async () => {
        function getUnconnected() {
            const socketLike = new MockSocketLikeBase();
            return {
                adapter$: Promise.fromResult(new SocketAdapter(socketLike)),
                socketLike,
                isConnected: false
            };
        }
        function getConnected() {
            const socketLike = new MockSocketLikeBase(x => x.makeAutoConnectable());
            const adapter$ = (async () => {
                const adapter = new SocketAdapter(socketLike);
                await adapter.connectAsync('foo', null, CancellationToken.none);
                return adapter;
            })();
            return { adapter$, socketLike, isConnected: true };
        }
        function* cases(): Iterable<{
            adapter$: Promise<SocketAdapter>,
            socketLike: MockSocketLikeBase,
            isConnected: boolean
        }> {
            yield getUnconnected();
            yield getConnected();
        }

        for (const _case of cases()) {
            const adapter = await _case.adapter$;
            expect(() => adapter.dispose()).not.toThrow();
            expect(() => adapter.dispose()).not.toThrow();

            expect(_case.socketLike.removeAllListeners).toHaveBeenCalledTimes(_case.isConnected ? 1 : 0);
            expect(_case.socketLike.unref).toHaveBeenCalledTimes(_case.isConnected ? 1 : 0);
            expect(_case.socketLike.destroy).toHaveBeenCalledTimes(_case.isConnected ? 1 : 0);
        }
    });
});
