import {
    AggregateError,
    ArgumentNullError,
    ArgumentOutOfRangeError,
    CancellationToken,
    CancellationTokenSource,
    ObjectDisposedError,
    PromisePal,
    Timeout,
    TimeSpan,
} from '../../../src/std';

import { expect } from 'chai';
import { __members } from '../../infrastructure';

import pluralize from 'pluralize';

const _nameof = <T>(name: keyof T) => name;

describe(`${CancellationTokenSource.name}'s`, () => {
    const method = function (name: string) {
        return `📞 ${name}`;
    };

    const __CancellationTokenSource = __members(CancellationTokenSource);

    describe('ctor', () => {
        function act(arg?: any) {
            return () => {
                const cts = new CancellationTokenSource(arg);
                cts.dispose();
            };
        }

        it(`should throw when called with invalid arguments`, () => {
            // anything other than number, TimeSpan or no args at all is not supported
            expect(act('some string')).to.throw(ArgumentOutOfRangeError);
            expect(act(true)).to.throw(ArgumentOutOfRangeError);
            expect(act(new Date())).to.throw(ArgumentOutOfRangeError);

            // negative spans are not supported (except for Infinity which is encoded as -1 millisecond)
            expect(act(-100)).to.throw(ArgumentOutOfRangeError);
            expect(act(TimeSpan.fromMinutes(-5))).to.throw(ArgumentOutOfRangeError);
        });

        it(`should not throw when called with valid arguments`, () => {
            expect(act()).not.to.throw();
            expect(act(0)).not.to.throw();
            expect(act(1)).not.to.throw();
            expect(act(100)).not.to.throw();
            expect(act(TimeSpan.zero)).not.to.throw();
            expect(act(TimeSpan.fromMilliseconds(1))).not.to.throw();
            expect(act(TimeSpan.fromDays(1))).not.to.throw();

            // Infinity should work
            expect(act(-1)).not.to.throw();
            expect(act(Timeout.infiniteTimeSpan)).not.to.throw();
        });
    });

    describe(__CancellationTokenSource.cancel, () => {
        let cts: CancellationTokenSource = undefined!;

        beforeEach(() => {
            cts = new CancellationTokenSource();
        });

        afterEach(() => {
            cts.dispose();
        });

        it('should not throw', () => {
            const act = () => cts.cancel();
            expect(act).not.to.throw();
        });

        it('should not throw when called a 2nd time', () => {
            const act = () => cts.cancel();
            act();

            expect(act).not.to.throw();
        });

        it(`should throw ${ObjectDisposedError.name} when called on a disposed CTS`, () => {
            cts.dispose();
            const act = () => cts.cancel();

            expect(act).to.throw(ObjectDisposedError);
        });

        describe(`👩‍🔬should throw ${AggregateError.name} with whatever the registered handler threw, when there's only one such handler`, () => {
            const handlerCounts = [1, 2, 3];
            const argsfor_ShouldThrowAggregate: [throwOnFirstError?: boolean][] = [[], [false]];

            for (const args of argsfor_ShouldThrowAggregate)
                for (const handlerCount of handlerCounts) {
                    it(`👩‍🔬when called with [...${args}], having ${handlerCount} ${pluralize(
                        'handler',
                        handlerCount,
                    )}`, () => {
                        class MockError extends Error {}

                        const originalErrors = Array.from({ length: handlerCount }).map(() => {
                            const originalError = new MockError();

                            cts.token.register(() => {
                                throw originalError;
                            });

                            return originalError;
                        });

                        const act = () => cts.cancel(...args);

                        expect(originalErrors).to.have.lengthOf(handlerCount);
                        expect(act)
                            .to.throw(AggregateError)
                            .that.satisfies((ae: AggregateError) => {
                                expect(ae.errors)
                                    .to.have.length(handlerCount)
                                    .and.to.include.members(originalErrors);
                                return true;
                            });
                    });
                }
        });
        describe(`👩‍🔬should rethrow whatever the first handler that had registered threw, all of which are throwing`, () => {
            const handlerCounts = [1, 2, 3];

            for (const handlerCount of handlerCounts) {
                it(`👩‍🔬having ${handlerCount} ${pluralize('handler', handlerCount)}`, () => {
                    class MockError extends Error {}

                    const originalErrors = Array.from({ length: handlerCount }).map(() => {
                        const originalError = new MockError();

                        cts.token.register(() => {
                            throw originalError;
                        });

                        return originalError;
                    });

                    const act = () => cts.cancel(true);

                    expect(originalErrors).to.have.lengthOf(handlerCount);
                    expect(act).to.throw(MockError).that.is.equal(originalErrors[0]);
                });
            }
        });
    });

    describe(__CancellationTokenSource.cancelAfter, () => {
        let cts: CancellationTokenSource = undefined!;

        beforeEach(() => {
            cts = new CancellationTokenSource();
        });

        afterEach(() => {
            cts.dispose();
        });

        it('should not throw', () => {
            const act = () => cts.cancelAfter(1);
            expect(act).not.to.throw();
        });

        it('should not throw when called a 2nd time', () => {
            const act = () => cts.cancelAfter(1);
            act();

            expect(act).not.to.throw();
        });

        describe(`should throw when called with invalid args`, () => {
            const groups = [
                {
                    error: ArgumentOutOfRangeError,
                    cases: [['some-string'], [true], [-100], [TimeSpan.fromDays(-2)]],
                },
                {
                    error: ArgumentNullError,
                    cases: [[], [null]],
                },
            ];

            for (const caseGroup of groups)
                describe(`should throw ${caseGroup.error.name}`, () => {
                    for (const _case of caseGroup.cases) {
                        it(`when called with ${JSON.stringify(_case)}`, () => {
                            const act = () => (cts as any).cancelAfter(..._case);

                            expect(act).to.throw(caseGroup.error);
                        });
                    }
                });
        });

        it(`should throw ${ObjectDisposedError.name} when called on a disposed CTS`, () => {
            cts.dispose();
            const act = () => cts.cancelAfter(1);

            expect(act).to.throw(ObjectDisposedError);
        });

        it(`should overwrite the effects of previous calls when called in due time`, async () => {
            cts.cancelAfter(TimeSpan.fromMinutes(10));
            cts.cancelAfter(TimeSpan.fromMilliseconds(1));

            await PromisePal.delay(50);

            expect(cts.token.isCancellationRequested).to.equal(true);
        });

        it(`should have no effect when called on an already canceled but not yet disposed CTS`, async () => {
            let invocationCount = 0;
            cts.token.register(() => invocationCount++);

            cts.cancel();
            expect(invocationCount).to.equal(1);

            const act = () => cts.cancelAfter(1);

            expect(act).not.to.throw();

            await PromisePal.delay(50);
            expect(invocationCount).to.equal(1);
        });

        const handlerCounts = [1, 2, 3];

        describe(`should cause the invocation of all handlers registered on the CT after the specified interval`, () => {
            for (const handlerCount of handlerCounts) {
                it(`having ${handlerCount} ${pluralize('handler', handlerCount)}`, async () => {
                    const spies = Array.from({ length: handlerCount }).map(() => {
                        const spy = { invoked: false };

                        cts.token.register(() => {
                            spy.invoked = true;
                        });

                        return spy;
                    });

                    function noneHaveBeenInvoked() {
                        for (const spy of spies) {
                            if (spy.invoked) {
                                return false;
                            }
                        }

                        return true;
                    }

                    function allHaveBeenInvoked() {
                        for (const spy of spies) {
                            if (!spy.invoked) {
                                return false;
                            }
                        }

                        return true;
                    }

                    expect(spies).to.have.lengthOf(handlerCount);
                    expect(noneHaveBeenInvoked()).to.equal(true);

                    cts.cancelAfter(TimeSpan.fromMilliseconds(50));
                    await PromisePal.delay(1);

                    expect(noneHaveBeenInvoked()).to.equal(true);
                    await PromisePal.delay(100);

                    expect(allHaveBeenInvoked()).to.equal(true);
                });
            }
        });
    });

    describe(__CancellationTokenSource.createLinkedTokenSource, () => {
        describe(`should throw when called with invalid args`, () => {
            const cases = [[], [123], [true, 123]] as any as Parameters<
                typeof CancellationTokenSource.createLinkedTokenSource
            >[];

            for (const _case of cases) {
                it(`when called with ${JSON.stringify(_case)}`, () => {
                    const act = () => CancellationTokenSource.createLinkedTokenSource(..._case);

                    expect(act).to.throw();
                });
            }
        });

        describe(`should not throw when called with valid args`, () => {
            const cts = {
                first: new CancellationTokenSource(),
                second: new CancellationTokenSource(),
            };

            const cases = [
                [CancellationToken.none],
                [CancellationToken.none, CancellationToken.none],
                [cts.first.token],
                [cts.first.token, cts.second.token],
            ] as Parameters<typeof CancellationTokenSource.createLinkedTokenSource>[];

            afterAll(() => {
                Object.values(cts).forEach(cts => cts.dispose());
            });

            for (const _case of cases) {
                it(`when called with ${JSON.stringify(_case.map(x => x.toString()))}`, () => {
                    const act = () => {
                        const linked = CancellationTokenSource.createLinkedTokenSource(..._case);
                        linked.dispose();
                    };

                    expect(act).not.to.throw();
                });
            }
        });
    });
});
