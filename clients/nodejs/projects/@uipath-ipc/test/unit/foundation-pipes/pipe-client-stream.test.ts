// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import { SocketLikeMocks } from './socket-like-mocks.test-helper';

import { PipeClientStream, PipeReader, ILogicalSocketFactory, ILogicalSocket, SocketAdapter, ISocketLike } from '@foundation/pipes';
import { CancellationToken, TimeSpan, CancellationTokenSource } from '@foundation/threading';
import { ArgumentNullError, TimeoutError, OperationCanceledError, PipeBrokenError, ObjectDisposedError, InvalidOperationError } from '@foundation/errors';
import { Trace } from '@foundation/utils';

use(spies);

describe(`foundation:pipes -> class:PipeClientStream`, () => {
    context(`method:connectAsync`, () => {
        it(`shouldn't throw provided falsy args but it should reject`, async () => {
            let promise: Promise<PipeClientStream> = null as any;

            (() => promise = PipeClientStream.connectAsync(null as any, 'name', null, false)).
                should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.null;

            await promise.
                should.eventually.be.rejectedWith(ArgumentNullError).
                with.property('paramName', 'factory');

            (() => promise = PipeClientStream.connectAsync((() => { }) as any, null as any, null, false)).
                should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.null;

            await promise.
                should.eventually.be.rejectedWith(ArgumentNullError).
                with.property('paramName', 'name');

            (() => promise = PipeClientStream.connectAsync((() => { }) as any, undefined as any, null, false)).
                should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.null;

            await promise.
                should.eventually.be.rejectedWith(ArgumentNullError).
                with.property('paramName', 'name');
        });

        it(`should resolve to a PipeClientStream provided truthy args`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;

            await PipeClientStream.connectAsync(factory, 'name', null, false).
                should.eventually.be.fulfilled.
                and.satisfy((x: any) => x instanceof PipeClientStream);
        });
    });

    context(`method:disposeAsync`, () => {
        it(`shouldn't throw or reject, even if called multiple times`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;

            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            let promise: Promise<void> = null as any;

            (() => promise = stream.disposeAsync()).should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;
            await promise.should.eventually.be.fulfilled;

            (() => promise = stream.disposeAsync()).should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;
            await promise.should.eventually.be.fulfilled;
        });
    });

    context(`method:readPartiallyAsync`, () => {
        it(`shouldn't throw but it should reject with ArgumentNullError provided a falsy destination`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            let promise: Promise<number> = null as any;
            (() => promise = stream.readPartiallyAsync(null as any)).should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;
            await promise.
                should.eventually.be.rejectedWith(ArgumentNullError).
                with.property('paramName', 'destination');
        });

        it(`should reject with ObjectDisposedError if the PipeClientStream had been disposed`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            await stream.disposeAsync();

            const destination = Buffer.alloc(10);

            let promise: Promise<number> = null as any;
            (() => promise = stream.readPartiallyAsync(destination)).should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;

            await promise.
                should.eventually.be.rejectedWith(ObjectDisposedError).
                with.property('objectName', 'PipeClientStream');
        });

        it(`should eventually be fulfilled when a single byte of data becomes available in the underlying ILogicalSocket`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            const destination = Buffer.alloc(10);
            const promise = stream.readPartiallyAsync(destination);

            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.delay(1);
            fulfilledSpy.should.not.have.been.called();

            mocks.emitData(Buffer.from([100]));
            await Promise.yield();

            fulfilledSpy.should.have.been.called.with(1);
            destination.readInt8(0).should.be.equal(100);
        });

        it(`should be fulfilled when some data was already available in the underlying ILogicalSocket`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            const source = Buffer.from([100, 101, 102]);
            mocks.emitData(source);

            const destination = Buffer.alloc(10);
            const promise = stream.readPartiallyAsync(destination);

            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.yield();
            fulfilledSpy.should.have.been.called.with(3);
            destination.subarray(0, 3).should.be.deep.equal(source);
        });

        it(`should eventually reject with PipeBrokerError when the underlying ILogicalSocket completes its data observable`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            const destination = Buffer.alloc(10);
            const promise = stream.readPartiallyAsync(destination);

            const rejectedSpy = spy((error: any) => {
                expect(error).to.be.instanceOf(PipeBrokenError);
            });
            promise.then(undefined, rejectedSpy);

            await Promise.delay(1);
            rejectedSpy.should.not.have.been.called();

            mocks.emitEnd();
            await Promise.yield();

            rejectedSpy.should.have.been.called();
        });

        it(`should reject with PipeBrokerError if the underlying ILogicalSocket had already completed its data observable`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            mocks.emitEnd();

            const destination = Buffer.alloc(10);
            const promise = stream.readPartiallyAsync(destination);

            const rejectedSpy = spy((error: any) => {
                expect(error).to.be.instanceOf(PipeBrokenError);
            });
            promise.then(undefined, rejectedSpy);

            await Promise.yield();

            rejectedSpy.should.have.been.called();
        });
    });

    context(`method:readAsync`, () => {
        it(`should reject with ArgumentNullError provided a falsy destination`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            let promise: Promise<void> = null as any;
            (() => promise = stream.readAsync(null as any)).should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;
            await promise.
                should.eventually.be.rejectedWith(ArgumentNullError).
                with.property('paramName', 'destination');
        });

        it(`should immediately resolve provided an empty destination`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            const destination = Buffer.alloc(0);

            const promise = stream.readAsync(destination);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.yield();
            fulfilledSpy.should.have.been.called();
        });

        it(`should resolve only after populating the entire destination buffer`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            mocks.emitData(Buffer.from([0, 1, 2, 3]));

            const destination = Buffer.alloc(10);

            const promise = stream.readAsync(destination);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.yield();
            fulfilledSpy.should.not.have.been.called();

            mocks.emitData(Buffer.from([4, 5, 6, 7, 8]));

            await Promise.yield();
            fulfilledSpy.should.not.have.been.called();

            mocks.emitData(Buffer.from([9, 10, 11, 12, 13, 14]));

            await Promise.yield();
            fulfilledSpy.should.have.been.called();

            destination.should.be.deep.equal(Buffer.from([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]));
        });
    });

    context(`method:writeAsync`, () => {
        it(`should reject with ArgumentNullError provided a falsy source`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            await stream.writeAsync(null as any).
                should.eventually.be.rejectedWith(ArgumentNullError).
                with.property('paramName', 'source');

            await stream.writeAsync(undefined as any).
                should.eventually.be.rejectedWith(ArgumentNullError).
                with.property('paramName', 'source');
        });

        it(`should reject with ObjectDisposedError if the PipeClientStream had been disposed`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            await stream.disposeAsync();

            const buffer = Buffer.alloc(10);
            await stream.writeAsync(buffer).
                should.eventually.be.rejectedWith(ObjectDisposedError).
                with.property('objectName', 'PipeClientStream');
        });

        it(`should immediately resolve provided an empty buffer`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            const buffer = Buffer.alloc(0);
            const promise = stream.writeAsync(buffer);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.yield();
            fulfilledSpy.should.have.been.called();
        });

        it(`should eventually be fulfilled after successfully writing the buffer to the underlying ILogicalSocket`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, false);

            let receivedCallback: () => void = null as any;

            mocks.socketLike.write = spy((_buffer: Buffer, cb: () => void) => {
                receivedCallback = cb;
                return false;
            }) as any;

            const buffer = Buffer.alloc(10);
            const promise = stream.writeAsync(buffer);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.yield();

            mocks.socketLike.write.should.have.been.called.with(buffer);
            expect(receivedCallback).not.to.be.null;

            await Promise.yield();

            fulfilledSpy.should.not.have.been.called();

            receivedCallback();

            await Promise.yield();

            fulfilledSpy.should.have.been.called();
        });
    });

    describe(`feature:trace`, () => {
        it(`should work when reading`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, true);

            const traceHandlerSpy = spy(() => { });
            Trace.addListener(traceHandlerSpy);

            const source = Buffer.from('buffer');
            const destination = Buffer.alloc(source.length);
            const promise = stream.readAsync(destination);

            mocks.emitData(source);
            await Promise.yield();

            traceHandlerSpy.should.have.been.called.with(source.toString(), 'io:read');
        });

        it(`should work when writing`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const logicalSocket = new SocketAdapter(mocks.socketLike);
            const factory: ILogicalSocketFactory = () => logicalSocket;
            const stream = await PipeClientStream.connectAsync(factory, 'name', null, true);

            mocks.socketLike.write = spy((_buffer: Buffer, _cb: () => void) => {
                return false;
            }) as any;

            const traceHandlerSpy = spy(() => { });
            Trace.addListener(traceHandlerSpy);

            const buffer = Buffer.from('buffer');
            stream.writeAsync(buffer);

            await Promise.yield();
            traceHandlerSpy.should.have.been.called.with(buffer.toString(), 'io:write');
        });
    });
});
