// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import { SocketLikeMocks } from './socket-like-mocks.test-helper';

import { Observable } from 'rxjs';

import { SocketAdapter, ISocketLike } from '@foundation/pipes';
import { CancellationToken, TimeSpan, CancellationTokenSource } from '@foundation/threading';
import { ArgumentNullError, TimeoutError, OperationCanceledError, PipeBrokenError, ObjectDisposedError, InvalidOperationError } from '@foundation/errors';

use(spies);

describe(`foundation:pipes -> class:SocketAdapter`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw provided a truthy ISocketLike`, () => {
            const socketLikeMock = SocketLikeMocks.createReadableMock();
            (() => new SocketAdapter(socketLikeMock)).should.not.throw();
        });

        it(`should throw provided a falsy ISocketLike`, () => {
            expect(() => new SocketAdapter(null as any)).to.throw(ArgumentNullError).that.has.property('maybeParamName', '_socketLike');
            expect(() => new SocketAdapter(undefined as any)).to.throw(ArgumentNullError).that.has.property('maybeParamName', '_socketLike');
        });
    });

    context(`method:connectAsync`, () => {
        it(`should reject provided a falsy path`, async () => {
            const socketLikeMock = SocketLikeMocks.createReadableMock();
            const instance = new SocketAdapter(socketLikeMock);

            await instance.connectAsync(null as any, null, CancellationToken.none).
                should.eventually.be.rejectedWith(ArgumentNullError).
                and.have.property('maybeParamName', 'path');

            await instance.connectAsync(undefined as any, null, CancellationToken.none).
                should.eventually.be.rejectedWith(ArgumentNullError).
                and.have.property('maybeParamName', 'path');
        });
        it(`should reject provided a falsy ct`, async () => {
            const instance = new SocketAdapter(SocketLikeMocks.createReadableMock());

            await instance.connectAsync('path', null, null as any).
                should.eventually.be.rejectedWith(ArgumentNullError).
                and.have.property('maybeParamName', 'cancellationToken');

            await instance.connectAsync('path', null, undefined as any).
                should.eventually.be.rejectedWith(ArgumentNullError).
                and.have.property('maybeParamName', 'cancellationToken');
        });
        it(`should resolve and connect immediately when ISocketLike connects immediately`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            const promise = instance.connectAsync('path', null, CancellationToken.none);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.yield();
            fulfilledSpy.should.have.been.called();
        });
        it(`should resolve and connect with a delay when ISocketLike connects with a delay which is smaller than the timeout`, async () => {
            const socketLikeMock = SocketLikeMocks.createDelayedConnectingMock(10);
            const instance = new SocketAdapter(socketLikeMock);

            await instance.connectAsync('path', TimeSpan.fromMilliseconds(20), CancellationToken.none).
                should.eventually.be.fulfilled;
        });
        it(`should reject when ISocketLike connects with a delay which is greater than the timeout`, async () => {
            const socketLikeMock = SocketLikeMocks.createDelayedConnectingMock(10);
            const instance = new SocketAdapter(socketLikeMock);

            await instance.connectAsync('path', TimeSpan.fromMilliseconds(5), CancellationToken.none).
                should.eventually.be.rejectedWith(TimeoutError);
        });

        context(`races between connecting, cancellation and timeout`, () => {
            function maybeCreateTimeSpan(milliseconds?: number): TimeSpan | null {
                if (milliseconds != null) {
                    return TimeSpan.fromMilliseconds(milliseconds);
                } else {
                    return null;
                }
            }
            function getCancellationToken(milliseconds?: number): CancellationToken {
                if (milliseconds != null) {
                    return new CancellationTokenSource(milliseconds).token;
                } else {
                    return CancellationToken.none;
                }
            }
            function arrangeRace(times: {
                connectingMilliseconds: number,
                timeoutMilliseconds?: number,
                cancellationMilliseconds?: number,
            }): {
                socketLike: ISocketLike,
                timeout: TimeSpan | null,
                cancellationToken: CancellationToken
            } {
                return {
                    socketLike: SocketLikeMocks.createDelayedConnectingMock(times.connectingMilliseconds),
                    timeout: maybeCreateTimeSpan(times.timeoutMilliseconds),
                    cancellationToken: getCancellationToken(times.cancellationMilliseconds)
                };
            }

            it(`should resolve when connecting === synchronous < cancellation === cancellation === 0`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 0,
                    timeoutMilliseconds: 0,
                    cancellationMilliseconds: 0
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.fulfilled;
            });

            it(`should reject with OperationCanceledError when connecting === cancellation === timeout === 1`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 1,
                    timeoutMilliseconds: 1,
                    cancellationMilliseconds: 1
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.rejectedWith(OperationCanceledError);
            });

            it(`should resolve when connecting === 1 < cancellation === timeout === ∞`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 1,
                    timeoutMilliseconds: undefined,
                    cancellationMilliseconds: undefined
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.fulfilled;
            });

            it(`should resolve when connecting === 1 < (cancellation === 10) < (timeout === ∞)`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 1,
                    timeoutMilliseconds: undefined,
                    cancellationMilliseconds: 10
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.fulfilled;
            });

            it(`should resolve when connecting === 1 < (cancellation === 10) < (timeout === 20)`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 1,
                    timeoutMilliseconds: 20,
                    cancellationMilliseconds: 10
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.fulfilled;
            });

            it(`should resolve when connecting === 1 < timeout === 10 < cancellation === 20`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 1,
                    timeoutMilliseconds: 10,
                    cancellationMilliseconds: 20
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.fulfilled;
            });

            it(`should reject with TimeoutError when timeout === 1 < cancellation === 10 < connecting === 20`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 20,
                    timeoutMilliseconds: 1,
                    cancellationMilliseconds: 10
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.rejectedWith(TimeoutError);
            });

            it(`should reject with TimeoutError when timeout === 1 < cancellation === connecting === 10`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 10,
                    timeoutMilliseconds: 1,
                    cancellationMilliseconds: 10
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.rejectedWith(TimeoutError);
            });

            it(`should reject with OperationCanceledError when cancellation === timeout === 1 < connecting === 2`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 2,
                    timeoutMilliseconds: 1,
                    cancellationMilliseconds: 1
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.rejectedWith(OperationCanceledError);
            });

            it(`should reject with OperationCanceledError when cancellation === timeout === 0 < connecting === 10`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 10,
                    timeoutMilliseconds: 0,
                    cancellationMilliseconds: 0
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.rejectedWith(OperationCanceledError);
            });

            it(`should reject with OperationCanceledError when cancellation === 1 < timeout === 10 < connecting === 20`, async () => {
                const arrangment = arrangeRace({
                    connectingMilliseconds: 20,
                    timeoutMilliseconds: 10,
                    cancellationMilliseconds: 1
                });
                const instance = new SocketAdapter(arrangment.socketLike);

                await instance.connectAsync('path', arrangment.timeout, arrangment.cancellationToken).
                    should.eventually.be.rejectedWith(OperationCanceledError);
            });
        });

        it(`should reject when ISocketLike connects with a delay which is greater than an eventual prescribed ct timeout`, async () => {
            const socketLikeMock = SocketLikeMocks.createDelayedConnectingMock(20);
            const instance = new SocketAdapter(socketLikeMock);

            const cts = new CancellationTokenSource(1);
            await instance.connectAsync('path', null, cts.token).
                should.eventually.be.rejectedWith(OperationCanceledError);
        });

        it(`should asynchronously rethrow the object reported by the underlying ISocketLike when it's not an instance of Error or its 'code' field is not 'EPIPE'`, async () => {
            const throwable = {};
            const socketLikeMock = SocketLikeMocks.creatingFailingMock(throwable);
            const instance = new SocketAdapter(socketLikeMock);

            await instance.connectAsync('path', null, CancellationToken.none).
                should.eventually.be.rejected.
                which.satisfies((x: any) => x === throwable);
        });

        it(`should asynchronously rethrow the object reported by the underlying ISocketLike when it's an instance of Error but its 'code' field is not 'EPIPE'`, async () => {
            const error = new Error();
            const socketLikeMock = SocketLikeMocks.creatingFailingMock(error);
            const instance = new SocketAdapter(socketLikeMock);

            await instance.connectAsync('path', null, CancellationToken.none).
                should.eventually.be.rejected.
                which.satisfies((x: any) => x === error);
        });

        it(`should reject with PipeBrokenError if the underlying ISocketLike reports an Error bearing a 'code' field with the value of 'EPIPE'`, async () => {
            const error: NodeJS.ErrnoException = new Error();
            error.code = 'EPIPE';

            const socketLikeMock = SocketLikeMocks.creatingFailingMock(error);
            const instance = new SocketAdapter(socketLikeMock);

            await instance.connectAsync('path', null, CancellationToken.none).
                should.eventually.be.rejected.
                with.instanceOf(PipeBrokenError);
        });

        it(`should reject with ObjectDisposedError if the SocketAdapter had been disposed`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);
            instance.dispose();
            await instance.connectAsync('path', null, CancellationToken.none).
                should.eventually.be.rejectedWith(ObjectDisposedError);
        });

        it(`should reject with InvalidOperationError if this is not the 1st call to connectAsync on the current SocketAdapter`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            const promise1 = instance.connectAsync('path', null, CancellationToken.none);
            await instance.connectAsync('path', null, CancellationToken.none).
                should.eventually.be.rejectedWith(InvalidOperationError);
        });
    });

    context(`method:dispose`, () => {
        it(`shouldn't throw when SocketAdapter is not connected even if called multiple times`, () => {
            const mockSocketLike = SocketLikeMocks.createDisconnectableMock();
            const instance = new SocketAdapter(mockSocketLike as any);

            expect(() => instance.dispose()).not.to.throw();
            expect(() => instance.dispose()).not.to.throw();
        });

        it(`shouldn't throw when SocketAdapter is connected if called multiple times`, async () => {
            const mockSocketLike = SocketLikeMocks.createDisconnectableMock();
            const instance = new SocketAdapter(mockSocketLike as any);
            await instance.connectAsync('path', null, CancellationToken.none);

            expect(() => instance.dispose()).not.to.throw();
            expect(() => instance.dispose()).not.to.throw();
        });
    });

    context(`property:data`, () => {
        it(`should not throw`, () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);
            (() => instance.data).should.not.throw();
        });

        it(`should return an Observable`, () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);
            instance.data.should.be.instanceOf(Observable);
        });

        it(`should return an observable which completes when the SocketAdapter is disposed`, () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            const completeSpy = spy(() => { });
            instance.data.subscribe({
                complete: completeSpy
            });

            instance.dispose();

            completeSpy.should.have.been.called;
        });

        it(`should return an observable which notifies new observers about completion if the SocketAdapter was already disposed`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            instance.dispose();

            const completeSpy = spy(() => { });
            instance.data.subscribe({
                complete: completeSpy
            });

            completeSpy.should.have.been.called;
        });

        it(`should return an observable which completes when the underlying ISocketLike emits end`, () => {
            const mock = SocketLikeMocks.createEndEmittingMock();
            const instance = new SocketAdapter(mock.socketLike);

            const completeSpy = spy(() => { });
            instance.data.subscribe({
                complete: completeSpy
            });

            mock.emitEnd();

            completeSpy.should.have.been.called;
        });

        it(`should return an observable which notifies new observers about completion if the underlying ISocketLike had already emitted end`, async () => {
            const mock = SocketLikeMocks.createEndEmittingMock();
            const instance = new SocketAdapter(mock.socketLike);

            mock.emitEnd();

            const completeSpy = spy(() => { });
            instance.data.subscribe({
                complete: completeSpy
            });

            completeSpy.should.have.been.called;
        });

        it(`should return an observable which notifies observers about buffers emitted by the underlying ISocketLike`, async () => {
            const mock = SocketLikeMocks.createEmittingMock();
            const instance = new SocketAdapter(mock.socketLike);

            await instance.connectAsync('path', null, CancellationToken.none);

            const nextSpy = spy((_buffer: Buffer) => { });
            instance.data.subscribe(nextSpy);

            const buffer = Buffer.from('buffer');
            mock.emitData(buffer);

            nextSpy.should.have.been.called.with(buffer);
        });
    });

    context(`method:writeAsync`, () => {
        it(`should reject with ArgumentNullError provided a falsy buffer`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            await instance.writeAsync(null as any, CancellationToken.none).
                should.eventually.rejectedWith(ArgumentNullError).
                which.has.property('maybeParamName', 'buffer');

            await instance.writeAsync(undefined as any, CancellationToken.none).
                should.eventually.rejectedWith(ArgumentNullError).
                which.has.property('maybeParamName', 'buffer');
        });

        it(`should reject with ArgumentNullError provided a falsy CancellationToken`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            await instance.writeAsync(Buffer.from('buffer'), null as any).
                should.eventually.rejectedWith(ArgumentNullError).
                which.has.property('maybeParamName', 'cancellationToken');

            await instance.writeAsync(Buffer.from('buffer'), undefined as any).
                should.eventually.rejectedWith(ArgumentNullError).
                which.has.property('maybeParamName', 'cancellationToken');
        });

        it(`should reject with ObjectDisposedError if the SocketAdapter had been disposed`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            instance.dispose();

            await instance.writeAsync(Buffer.from('buffer'), CancellationToken.none).
                should.eventually.rejectedWith(ObjectDisposedError);
        });

        it(`should reject with InvalidOperationError if the SocketAdapter isn't connected`, async () => {
            const socketLikeMock = SocketLikeMocks.createImmediatelyConnectingMock();
            const instance = new SocketAdapter(socketLikeMock);

            await instance.writeAsync(Buffer.from('buffer'), CancellationToken.none).
                should.eventually.be.rejectedWith(InvalidOperationError);
        });

        it(`should resolve if the SocketAdapter the provided buffer and ct are truthy, the SocketAdapter is connected and hasn't been disposed, the underlying ISocketLike doesn't report any errors and the ct isn't signalled`, async () => {
            const socketLikeMock = SocketLikeMocks.createWritableMock();
            const instance = new SocketAdapter(socketLikeMock);

            await instance.connectAsync('path', null, CancellationToken.none);

            await instance.writeAsync(Buffer.from('buffer'), CancellationToken.none).
                should.eventually.be.fulfilled;
        });

        it(`should reject with the error the underlying ISocketLike is reporting while writing to it`, async () => {
            const error = new Error();
            const socketLikeMock = SocketLikeMocks.createWritableMock();
            (socketLikeMock as any).write = spy((_buffer: Uint8Array | string, cb?: (err?: Error) => void): boolean => {
                expect(cb).not.to.be.null;
                expect(cb).not.to.be.undefined;

                (async () => {
                    (cb as any)(error);
                })();
                return false;
            });

            const instance = new SocketAdapter(socketLikeMock);
            await instance.connectAsync('path', null, CancellationToken.none);

            await instance.writeAsync(Buffer.from('buffer'), CancellationToken.none).
                should.be.eventually.rejectedWith(Error).
                that.is.equal(error);
        });

        it(`should immediately reject with OperationCanceledError when provided a ct which is already canceled`, async () => {
            const socketLikeMock = SocketLikeMocks.createWritableMock();
            (socketLikeMock as any).write = spy((_buffer: Uint8Array | string, cb?: (err?: Error) => void): boolean => {
                expect(cb).not.to.be.null;
                expect(cb).not.to.be.undefined;

                (async () => {
                    (cb as any)();
                })();
                return false;
            });
            const instance = new SocketAdapter(socketLikeMock);
            await instance.connectAsync('path', null, CancellationToken.none);

            const cts = new CancellationTokenSource();
            cts.cancel();
            const promise = instance.writeAsync(Buffer.from('buffer'), cts.token);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy, _ => { });

            await Promise.yield();
            fulfilledSpy.should.have.been.called;
        });
    });
});
