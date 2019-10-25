// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, should, spy, use } from 'chai';
import spies from 'chai-spies';

import { CancellationTokenSource, CancellationToken, TimeSpan } from '@foundation/threading';
import { ObjectDisposedError, ArgumentError, AggregateError } from '@foundation/errors';

use(spies);

describe(`foundation:threading -> class:CancellationTokenSource`, () => {
    const cases: Array<{
        factory: () => CancellationTokenSource;
        isNegative?: boolean,
        cancelAfterNotCalled?: boolean,
        initialDelay?: TimeSpan
    }> = [
            { isNegative: true, factory: () => new CancellationTokenSource(-1), },
            { isNegative: true, factory: () => new CancellationTokenSource(TimeSpan.fromMilliseconds(-1)), },
            { cancelAfterNotCalled: true, factory: () => new CancellationTokenSource(), },
            { initialDelay: TimeSpan.fromMilliseconds(0), factory: () => new CancellationTokenSource(0), },
            { initialDelay: TimeSpan.fromMilliseconds(10), factory: () => new CancellationTokenSource(10), },
            { initialDelay: TimeSpan.fromMilliseconds(0), factory: () => new CancellationTokenSource(TimeSpan.zero), },
            { initialDelay: TimeSpan.fromMilliseconds(10), factory: () => new CancellationTokenSource(TimeSpan.fromMilliseconds(10)) }
        ];

    const cancelCalls: Array<{
        actualCall: (instance: CancellationTokenSource) => void,
        isNegative?: boolean,
        delay?: TimeSpan
    }> = [
            { isNegative: true, actualCall: x => x.cancelAfter(-1), },
            { isNegative: true, actualCall: x => x.cancelAfter(TimeSpan.fromMilliseconds(-1)), },
            { delay: TimeSpan.fromMilliseconds(10), actualCall: x => x.cancelAfter(10), },
            { delay: TimeSpan.fromMilliseconds(10), actualCall: x => x.cancelAfter(TimeSpan.fromMilliseconds(10)), },
            { delay: TimeSpan.fromMilliseconds(0), actualCall: x => x.cancelAfter(0), },
            { delay: TimeSpan.fromMilliseconds(0), actualCall: x => x.cancelAfter(TimeSpan.zero), }
        ];

    context(`ctor`, () => {
        it(`shouldn't throw when provided a non-negative delay`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                expect(_case.factory).not.to.throw();
            }
        });
        it(`should throw when provided a negative delay`, () => {
            for (const _case of cases.filter(x => !!x.isNegative)) {
                expect(_case.factory).to.throw(ArgumentError).with.property('paramName', 'arg0');
            }
        });
    });

    context(`method:dispose`, () => {
        it(`shouldn't throw (even if called multiple times)`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                const cts = _case.factory();

                expect(() => cts.dispose()).not.to.throw();
                expect(() => cts.dispose()).not.to.throw();
            }
        });
        it(`should cause eventual calls to other methods to throw ObjectDisposedError`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                const cts = _case.factory();

                cts.dispose();

                expect(() => cts.cancel()).to.throw(ObjectDisposedError).with.property('objectName', 'CancellationTokenSource');
                expect(() => cts.cancel(false)).to.throw(ObjectDisposedError).with.property('objectName', 'CancellationTokenSource');
                expect(() => cts.cancel(true)).to.throw(ObjectDisposedError).with.property('objectName', 'CancellationTokenSource');
                expect(() => cts.cancelAfter(TimeSpan.fromSeconds(10))).to.throw(ObjectDisposedError).with.property('objectName', 'CancellationTokenSource');
            }
        });
    });

    context(`method:cancelAfter`, () => {
        it(`shouldn't throw on an instance which is not yet disposed, when provided a non-negative delay (even if called multiple times)`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const _call of cancelCalls.filter(x => !x.isNegative)) {
                    const cts = _case.factory();
                    expect(() => _call.actualCall(cts)).not.to.throw();
                    expect(() => _call.actualCall(cts)).not.to.throw();
                }
            }
        });
        it(`should throw when provided a negative delay (even if the instance is not yet disposed)`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const _call of cancelCalls.filter(x => !!x.isNegative)) {
                    const cts = _case.factory();
                    expect(() => _call.actualCall(cts)).
                        to.
                        throw(ArgumentError);
                }
            }
        });
        it(`should cause the associated ct to eventually transition to the canceled state`, async () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const _call of cancelCalls.filter(x => !x.isNegative)) {
                    if (!_call.delay) { continue; }

                    const cts = _case.factory();
                    const token = cts.token;

                    _call.actualCall(cts);
                    expect(token.isCancellationRequested).to.be.false;
                    await Promise.delay(_call.delay.add(_call.delay));
                    expect(token.isCancellationRequested).to.be.true;
                }
            }
        });
    });

    context(`method:cancel`, () => {
        it(`shouldn't throw when the associated ct has 0 registered callbacks (even if called multiple times)`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                const cts = _case.factory();
                expect(() => cts.cancel()).not.to.throw();
                expect(() => cts.cancel()).not.to.throw();
                expect(() => cts.cancel(true)).not.to.throw();
                expect(() => cts.cancel(true)).not.to.throw();
                expect(() => cts.cancel(false)).not.to.throw();
                expect(() => cts.cancel(false)).not.to.throw();
            }
        });

        it(`shouldn't throw when the associated ct has registered callbacks which don't throw (even if called multiple times)`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                const cts = _case.factory();
                cts.token.register(() => { });
                cts.token.register(() => { });

                expect(() => cts.cancel()).not.to.throw();
                expect(() => cts.cancel()).not.to.throw();
                expect(() => cts.cancel(true)).not.to.throw();
                expect(() => cts.cancel(true)).not.to.throw();
                expect(() => cts.cancel(false)).not.to.throw();
                expect(() => cts.cancel(false)).not.to.throw();
            }
        });

        it(`should throw what the associated ct's single callback throws when throwOnFirstError is true`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                const error = new Error();
                const cts = _case.factory();

                cts.token.register(() => { throw error; });

                expect(() => cts.cancel(true)).to.throw().equal(error);
            }
        });

        it(`should throw what the associated ct's single callback throws wrapped in AggregateError when throwOnFirstError is falsy`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const arg of [false, undefined]) {
                    const error = new Error();
                    const cts = _case.factory();

                    cts.token.register(() => { throw error; });

                    expect(() => cts.cancel(arg)).to.throw(AggregateError).with.property('errors').contains(error);
                }
            }
        });

        it(`should cause the transition of the associated ct to the canceled state`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const arg of [false, undefined]) {
                    const cts = _case.factory();
                    const token = cts.token;

                    cts.cancel(arg);
                    expect(token.isCancellationRequested).to.be.true;
                }
            }
        });

        it(`shouldn't cause the invocation of callbacks a 2nd time`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const arg of [false, undefined]) {
                    const cts = _case.factory();

                    const spyHandler = spy(() => { });
                    cts.token.register(spyHandler);

                    cts.cancel(arg);
                    expect(spyHandler).to.have.been.called();

                    cts.cancel(arg);
                    expect(spyHandler).not.to.have.been.called.twice;
                }
            }
        });

        it(`shouldn't throw when called a 2nd time regardless of the situation`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const shouldThrowInCallback of [true, false]) {
                    for (const argFirstTime of [true, false, undefined]) {
                        for (const argSecondTime of [true, false, undefined]) {
                            const error = new Error();
                            const cts = _case.factory();

                            if (shouldThrowInCallback) {
                                cts.token.register(() => { throw error; });
                            } else {
                                cts.token.register(() => { });
                            }

                            try { cts.cancel(argFirstTime); } catch { }
                            expect(() => cts.cancel(argSecondTime)).not.to.throw();
                        }
                    }
                }
            }
        });

        it(`should allow for all of the associated ct's callbacks to be called regardless of which one throws when throwOnFirstError is falsy`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                for (const arg of [false, undefined]) {
                    const error = new Error();
                    const cts = _case.factory();

                    const spyHandler = spy(() => { });
                    cts.token.register(() => { throw error; });
                    cts.token.register(spyHandler);

                    try {
                        cts.cancel(arg);
                    } catch (_) {
                    }

                    expect(spyHandler).to.have.been.called();
                }
            }
        });

        it(`should stop the invocation of the associated ct's callbacks registered after a failing onethrowOnFirstError is true`, () => {
            for (const _case of cases.filter(x => !!x.cancelAfterNotCalled)) {
                const error = new Error();
                const cts = _case.factory();

                const spyHandler = spy(() => { });
                cts.token.register(() => { throw error; });
                cts.token.register(spyHandler);

                try {
                    cts.cancel(true);
                } catch (_) {
                }

                expect(spyHandler).not.to.have.been.called();
            }
        });
    });

    context(`property:token`, () => {
        it(`shouldn't throw`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                const cts = _case.factory();
                expect(() => cts.token).not.to.throw();
            }
        });
        it(`should return strictly equal references every time it's called`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                const cts = _case.factory();
                expect(cts.token).
                    to.
                    equal(cts.token).
                    to.
                    equal(cts.token);
            }
        });
        it(`should return a truthy reference`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                const cts = _case.factory();
                expect(cts.token).not.to.equal(null);
                expect(cts.token).not.to.equal(undefined);
            }
        });
        it(`should return a ct`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                const cts = _case.factory();
                expect(cts.token).instanceOf(CancellationToken);
            }
        });
        it(`should return a ct which is not canceled`, () => {
            for (const _case of cases.filter(x => !x.isNegative)) {
                const cts = _case.factory();
                expect(cts.token.isCancellationRequested).to.be.false;
            }
        });
        it(`should return a ct which whose transition to the canceled state is imminent (when the cts was created with a valid delay)`, async () => {
            for (const _case of cases.filter(x => !x.cancelAfterNotCalled)) {
                if (!_case.initialDelay) { continue; }

                const initialDelay = _case.initialDelay;
                const cts = _case.factory();
                const token = cts.token;
                expect(token.isCancellationRequested).to.be.false;

                await Promise.delay(initialDelay.add(initialDelay));
                expect(token.isCancellationRequested).to.be.true;
            }
        });
    });
});
