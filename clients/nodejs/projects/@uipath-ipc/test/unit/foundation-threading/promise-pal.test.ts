// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, should, spy, use, assert } from 'chai';
import spies from 'chai-spies';
import chaiAsPromised from 'chai-as-promised';

import { OperationCanceledError, ArgumentNullError, ArgumentError } from '@foundation/errors';
import { TimeSpan, CancellationTokenSource, CancellationToken } from '@foundation/threading';

use(spies);
use(chaiAsPromised);

describe(`foundation:threading -> class:PromisePal`, () => {
    context(`property:completedPromise`, () => {
        it(`shouldn't throw`, () => {
            expect(() => Promise.completedPromise).not.to.throw();
        });
        it(`should return a Promise`, () => {
            expect(Promise.completedPromise).to.be.instanceOf(Promise);
        });
        it(`should return the same thing over and over`, () => {
            expect(Promise.completedPromise).to.equal(Promise.completedPromise);
        });
        it(`should return a Promise which is completed successfully`, async () => {
            await Promise.completedPromise;
        });
    });

    const _successfulCases = [
        undefined,
        null,
        '',
        'foo',
        0,
        1,
        false,
        true
    ];
    const _failureCases = [
        new Error(),
        new Error('foo')
    ];

    context(`method:fromResult`, () => {
        it(`shouldn't throw`, () => {
            for (const _case of _successfulCases) {
                expect(() => Promise.fromResult(_case)).not.to.throw();
            }
        });
        it(`should return a Promise`, () => {
            for (const _case of _successfulCases) {
                expect(Promise.fromResult(_case)).to.be.instanceOf(Promise);
            }
        });
        it(`should return a Promise which resolves immediately to the value provided as argument`, async () => {
            for (const _case of _successfulCases) {
                const promise = Promise.fromResult(_case);
                const _then = spy(() => { });
                promise.then(_then);
                await Promise.yield();
                expect(_then).to.have.been.called.with(_case);
            }
        });
    });

    context(`method:fromError`, () => {
        it(`shouldn't throw`, () => {
            for (const _case of _failureCases) {
                expect(() => Promise.fromError(_case)).not.to.throw();
            }
        });
        it(`should return a Promise`, () => {
            for (const _case of _failureCases) {
                expect(Promise.fromError(_case)).to.be.instanceOf(Promise);
            }
        });
        it(`should return a Promise which rejects immediately to the Error provided as argument`, async () => {
            for (const _case of _failureCases) {
                const promise = Promise.fromError(_case);
                const _catch = spy(() => { });
                promise.then(() => { }, _catch);
                await Promise.yield();
                expect(_catch).to.have.been.called.with(_case);
            }
        });
    });

    context(`method:fromCanceled`, () => {
        it(`shouldn't throw`, () => {
            expect(() => Promise.fromCanceled()).not.to.throw();
        });
        it(`should return a Promise`, () => {
            expect(Promise.fromCanceled()).to.be.instanceOf(Promise);
        });
        it(`should return a Promise which rejects immediately to OperationCanceledError`, async () => {
            const promise = Promise.fromCanceled();
            const _catch = spy((x: any) => expect(x).to.be.instanceOf(OperationCanceledError));
            promise.then(() => { }, _catch);
            await Promise.yield();
            expect(_catch).to.have.been.called();
        });
    });

    context(`method:delay`, () => {
        it(`shouldn't throw provided a non-negative number (delay milliseconds) and no ct`, () => {
            expect(() => Promise.delay(0)).not.to.throw();
            expect(() => Promise.delay(1)).not.to.throw();
        });
        it(`shouldn't throw provided a non-negative TimeSpan (delay) and no ct`, () => {
            expect(() => Promise.delay(TimeSpan.fromMilliseconds(0))).not.to.throw();
            expect(() => Promise.delay(TimeSpan.fromMilliseconds(1))).not.to.throw();
        });
        it(`shouldn't throw provided a non-negative number (delay milliseconds) and a ct`, () => {
            expect(() => Promise.delay(0, CancellationToken.none)).not.to.throw();
            expect(() => Promise.delay(1, CancellationToken.none)).not.to.throw();

            const ct = new CancellationTokenSource().token;

            expect(() => Promise.delay(0, ct)).not.to.throw();
            expect(() => Promise.delay(1, ct)).not.to.throw();
        });
        it(`shouldn't throw provided a non-negative TimeSpan (delay) and a ct`, () => {
            expect(() => Promise.delay(TimeSpan.fromMilliseconds(0), CancellationToken.none)).not.to.throw();
            expect(() => Promise.delay(TimeSpan.fromMilliseconds(1), CancellationToken.none)).not.to.throw();

            const ct = new CancellationTokenSource().token;

            expect(() => Promise.delay(TimeSpan.fromMilliseconds(0), ct)).not.to.throw();
            expect(() => Promise.delay(TimeSpan.fromMilliseconds(1), ct)).not.to.throw();
        });

        it(`should throw provided 0 args`, () => {
            expect(() => (Promise.delay as any as () => {})()).to.throw(ArgumentNullError).property('maybeParamName', 'delayOrMillisecondsDelay');
        });

        it(`should throw provided a 1st arg of type other than number or TimeSpan`, () => {
            expect(() => Promise.delay(true as any)).to.throw(ArgumentError).property('message', 'Expecting a number or a TimeSpan as the first argument.');
            expect(() => Promise.delay({} as any)).to.throw(ArgumentError).property('message', 'Expecting a number or a TimeSpan as the first argument.');
        });

        it(`should throw provided a negative number (delay milliseconds)`, () => {
            expect(() => Promise.delay(-1)).to.throw(ArgumentError).
                that.satisfies((error: ArgumentError) => {
                    expect(error).to.have.property('maybeParamName', 'millisecondsDelay');
                    expect(error).to.have.property('message').that.satisfies((message: string) => message.startsWith('Cannot delay for a negative timespan.'));
                    return true;
                });
        });

        it(`should throw provided a negative TimeSpan (delay)`, () => {
            expect(() => Promise.delay(TimeSpan.fromMilliseconds(-1))).to.throw(ArgumentError).
                that.satisfies((error: ArgumentError) => {
                    expect(error).to.have.property('maybeParamName', 'delay');
                    expect(error).to.have.property('message').that.satisfies((message: string) => message.startsWith('Cannot delay for a negative timespan.'));
                    return true;
                });
        });

        const _cases: Array<{ arg: number | TimeSpan, millis: number }> = [
            { arg: 0, millis: 0 },
            { arg: 1, millis: 1 },
            { arg: TimeSpan.fromMilliseconds(0), millis: 0 },
            { arg: TimeSpan.fromMilliseconds(1), millis: 1 }];

        it(`should return a Promise`, () => {
            for (const _case of _cases) {
                expect(Promise.delay(_case.arg)).to.be.instanceOf(Promise);
            }
        });

        it(`should return a Promise which resolves in at most double the provided time`, async () => {
            for (const _case of _cases) {
                const promise = Promise.delay(_case.arg);
                const handler = spy(() => { });
                promise.then(handler);
                await new Promise<void>(resolve => {
                    setTimeout(resolve, _case.millis * 2);
                });
                expect(handler).to.have.been.called();
            }
        });

        it(`should return a Promise which rejects as canceled when canceling the provided ct's source`, async () => {
            for (const _case of _cases) {
                const cts = new CancellationTokenSource();
                const promise = Promise.delay(_case.arg, cts.token);
                const _catch = spy((error: any) => {
                    expect(error).to.be.instanceOf(OperationCanceledError);
                });
                promise.then(() => { }, _catch);

                cts.cancel();
                await Promise.yield();
                expect(_catch).to.have.been.called();
            }
        });
    });
});
