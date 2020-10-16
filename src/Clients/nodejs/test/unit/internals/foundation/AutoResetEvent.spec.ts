import {
    concatArgs,
    constructing,
    forInstance,
} from '@test-helpers';

import {
    AsyncAutoResetEvent,
    ArgumentOutOfRangeError,
    CancellationToken,
    CancellationTokenSource,
    OperationCanceledError,
    PromiseStatus,
} from '@foundation';

describe(`internals`, () => {
    describe(`the AsyncAutoResetEvent class`, () => {
        context(`the constructor`, () => {
            context(`should not throw`, () => {
                for (const args of [
                    [],
                    [false],
                    [true],
                ] as never[][]) {
                    it(`should not throw when called with args: [${concatArgs(args)}]`, () => {
                        constructing(AsyncAutoResetEvent, ...args).should.not.throw();
                    });
                }
            });

            context(`should throw`, () => {
                for (const args of [
                    [0],
                    [1],
                    ['some string'],
                    [() => { }],
                    [{}],
                    [[]],
                    [Symbol()],
                ] as never[][]) {
                    it(`should throw ArgumentOutOfRangeError when called with args: [${concatArgs(args)}]`, () => {
                        constructing(AsyncAutoResetEvent, ...args).should.throw(ArgumentOutOfRangeError);
                    });
                }
            });
        });

        context(`the set method`, () => {
            it(`should not throw when called on an initially not signaled AutoResetEvent with zero awaiters`, () => {
                const event = new AsyncAutoResetEvent();
                forInstance(event).calling('set').should.not.throw();
            });

            it(`should not throw when called on an initially not signaled AutoResetEvent with awaiters`, () => {
                const event = new AsyncAutoResetEvent();
                event.waitOne(CancellationToken.none).observe();
                forInstance(event).calling('set').should.not.throw();
            });

            it(`should not throw when called on an initially signaled AutoResetEvent`, () => {
                const event = new AsyncAutoResetEvent(true);
                forInstance(event).calling('set').should.not.throw();
            });
        });

        context(`the waitOne method`, () => {
            it(`should return immediately on an already signaled AutoResetEvent`, async () => {
                const event = new AsyncAutoResetEvent(true);
                const spy = event.waitOne().spy();
                await Promise.yield();
                spy.status.should.be.eq(PromiseStatus.Succeeded);
            });

            it(`should return as soon as the AutoResetEvent becomes signaled`, async () => {
                const event = new AsyncAutoResetEvent(false);
                const spy = event.waitOne().spy();
                await Promise.delay(100);
                spy.status.should.be.eq(PromiseStatus.Running);
                event.set();
                await Promise.yield();
                spy.status.should.be.eq(PromiseStatus.Succeeded);
            });

            it(`should throw as soon as the provided CancellationToken becomes signaled if that wins the race with the AutoResetEvent becomming signaled`, async () => {
                const event = new AsyncAutoResetEvent(false);
                const cts = new CancellationTokenSource();
                const spy = event.waitOne(cts.token).spy();
                cts.cancel();
                await Promise.yield();
                spy.status.should.be.eq(PromiseStatus.Canceled);
                await spy.promise.should.eventually.be.rejectedWith(OperationCanceledError);
            });

            it(`should only return for one caller when the AutoResetEvent becomes signaled`, async () => {
                const event = new AsyncAutoResetEvent(false);
                const spy1 = event.waitOne().spy();
                const spy2 = event.waitOne().spy();
                event.set();
                await Promise.yield();
                spy1.status.should.be.eq(PromiseStatus.Succeeded);
                spy2.status.should.be.eq(PromiseStatus.Running);
            });
        });
    });
});
