// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';
import { SocketLikeMocks } from './socket-like-mocks.test-helper';

import { PipeReader, SocketAdapter } from '@foundation/pipes';
import { CancellationToken, } from '@foundation/threading';
import { ArgumentNullError, ObjectDisposedError, InvalidOperationError } from '@foundation/errors';

use(spies);

describe(`foundation:pipes -> class:PipeReader`, () => {
    context(`ctor`, () => {
        it(`should throw provided a falsy ILogicalSocket`, () => {
            (() => new PipeReader(null as any)).should.throw(ArgumentNullError).
                that.has.property('maybeParamName', 'socket');

            (() => new PipeReader(undefined as any)).should.throw(ArgumentNullError).
                that.has.property('maybeParamName', 'socket');
        });
        it(`shouldn't throw provided a truthy ILogicalSocket`, () => {
            const logicalSocket = new SocketAdapter(SocketLikeMocks.createImmediatelyConnectingMock());
            (() => new PipeReader(logicalSocket)).should.not.throw();
        });
    });

    context(`method:disposeAsync`, () => {
        it(`shouldn't throw or reject for a not yet activated PipeReader, even when called multiple times`, async () => {
            const pipeReader = new PipeReader(new SocketAdapter(SocketLikeMocks.createImmediatelyConnectingMock()));
            let promise: Promise<void> = null as any;

            (() => promise = pipeReader.disposeAsync()).should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;
            await promise.should.eventually.be.fulfilled;

            (() => promise = pipeReader.disposeAsync()).should.not.throw();
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;
            await promise.should.eventually.be.fulfilled;
        });
    });

    context(`method:readPartiallyAsync`, () => {
        it(`shouldn't throw provided falsy args (because it should return a Promise which will eventually reject)`, async () => {
            const pipeReader = new PipeReader(new SocketAdapter(SocketLikeMocks.createImmediatelyConnectingMock()));

            (() => pipeReader.readPartiallyAsync(null as any, CancellationToken.none)).should.not.throw();
            (() => pipeReader.readPartiallyAsync(undefined as any, CancellationToken.none)).should.not.throw();

            (() => pipeReader.readPartiallyAsync(Buffer.from('buffer'), null as any)).should.not.throw();
            (() => pipeReader.readPartiallyAsync(Buffer.from('buffer'), undefined as any)).should.not.throw();
        });

        it(`should be rejected with ArgumentNullError provided falsy args`, async () => {
            const pipeReader = new PipeReader(new SocketAdapter(SocketLikeMocks.createImmediatelyConnectingMock()));

            await pipeReader.readPartiallyAsync(null as any, CancellationToken.none).
                should.eventually.be.rejectedWith(ArgumentNullError).
                that.has.property('maybeParamName', 'destination');

            await pipeReader.readPartiallyAsync(undefined as any, CancellationToken.none).
                should.eventually.be.rejectedWith(ArgumentNullError).
                that.has.property('maybeParamName', 'destination');

            await pipeReader.readPartiallyAsync(Buffer.from('buffer'), null as any).
                should.eventually.be.rejectedWith(ArgumentNullError).
                that.has.property('maybeParamName', 'cancellationToken');

            await pipeReader.readPartiallyAsync(Buffer.from('buffer'), undefined as any).
                should.eventually.be.rejectedWith(ArgumentNullError).
                that.has.property('maybeParamName', 'cancellationToken');
        });

        it(`should be rejected with ObjectDisposedError if the PipeReader had been disposed`, async () => {
            const pipeReader = new PipeReader(new SocketAdapter(SocketLikeMocks.createImmediatelyConnectingMock()));

            await pipeReader.disposeAsync();

            await pipeReader.readPartiallyAsync(Buffer.from('buffer'), CancellationToken.none).
                should.eventually.be.rejectedWith(ObjectDisposedError).
                that.has.property('objectName', 'PipeReader');
        });

        it(`should be rejected with InvalidOperationError if another call is already in progrss`, async () => {
            const pipeReader = new PipeReader(new SocketAdapter(SocketLikeMocks.createImmediatelyConnectingMock()));

            pipeReader.readPartiallyAsync(Buffer.from('buffer'), CancellationToken.none);
            await pipeReader.readPartiallyAsync(Buffer.from('buffer'), CancellationToken.none).
                should.eventually.be.rejectedWith(InvalidOperationError).
                that.has.property('message', 'Cannot read twice concurrently.');
        });

        it(`should be fulfilled and transfer the already available data from the underlying ILogicalSocket even when that data was smaller than the requested size`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const pipeReader = new PipeReader(new SocketAdapter(mocks.socketLike));

            const source = Buffer.from('buffer');
            mocks.emitData(source);

            const destination = new Buffer(20);
            await pipeReader.readPartiallyAsync(destination, CancellationToken.none).
                should.eventually.be.fulfilled.
                which.equals(source.length);

            destination.subarray(0, source.length).
                should.deep.equal(source);
        });

        it(`should be fulfilled and transfer the a subset of the already available data from the underlying ILogicalSocket even when more data was available than the requested size`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const pipeReader = new PipeReader(new SocketAdapter(mocks.socketLike));

            const source = Buffer.from('buffer');
            mocks.emitData(source);

            const destination = new Buffer(2);
            await pipeReader.readPartiallyAsync(destination, CancellationToken.none).
                should.eventually.be.fulfilled.
                which.equals(destination.length);

            destination.should.deep.equal(source.subarray(0, destination.length));
        });

        it(`should be fulfilled and transfer the already available data from the underlying ILogicalSocket when the requested data size matches the already available data size`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const pipeReader = new PipeReader(new SocketAdapter(mocks.socketLike));

            const source = Buffer.from('buffer');
            mocks.emitData(source);

            const destination = new Buffer(source.length);
            await pipeReader.readPartiallyAsync(destination, CancellationToken.none).
                should.eventually.be.fulfilled.
                which.equals(source.length);

            destination.should.deep.equal(source);
        });

        it(`should defer being fulfilled until at least one byte of data becomes available`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const pipeReader = new PipeReader(new SocketAdapter(mocks.socketLike));

            const destination = new Buffer(10);

            const promise = pipeReader.readPartiallyAsync(destination, CancellationToken.none);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            await Promise.delay(1);
            fulfilledSpy.should.not.have.been.called;

            const source = Buffer.from('b');
            mocks.emitData(source);

            await Promise.yield();
            fulfilledSpy.should.have.been.called.with(1);
            destination.readInt8(0).should.be.equal(source.readInt8(0));
        });

        it(`shouldn't reject and it should eventually become fulfilled when the underlying ILogicalSocket's data observable completes`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const pipeReader = new PipeReader(new SocketAdapter(mocks.socketLike));

            const destination = new Buffer(10);

            const promise = pipeReader.readPartiallyAsync(destination, CancellationToken.none);
            const fulfilledSpy = spy(() => { });
            promise.then(fulfilledSpy);

            mocks.emitEnd();

            await Promise.yield();
            fulfilledSpy.should.have.been.called.with(0);
        });

        it(`shouldn't reject and it should become fulfilled when the underlying ILogicalSocket's data observable had already completed`, async () => {
            const mocks = SocketLikeMocks.createEmittingMock();
            const pipeReader = new PipeReader(new SocketAdapter(mocks.socketLike));

            mocks.emitEnd();

            const destination = new Buffer(10);
            await pipeReader.readPartiallyAsync(destination, CancellationToken.none).
                should.eventually.be.fulfilled.
                which.is.equal(0);
        });
    });
});
