import {
    ArgumentNullError,
    AsyncAutoResetEvent,
    CancellationTokenSource,
    OperationCanceledError,
    PromisePal,
    Timeout,
    TimeSpan,
} from '../../../src/std';

import { expect } from 'chai';
import { PromiseStatus } from '../../../src/std/bcl/promises/PromiseStatus';

describe(`${PromisePal.name}'s`, () => {
    describe(`🌿 "never" static property`, () => {
        it(`should not throw`, () => {
            const act = () => PromisePal.never;
            expect(act).not.to.throw();
        });

        describe(`📦 the returned object`, () => {
            it(`should not be null or undefined`, () => {
                expect(PromisePal.never).not.to.be.null.and.not.to.be.undefined;
            });

            it(`should be the same reference every time`, () => {
                expect(PromisePal.never).to.equal(PromisePal.never);
            });

            it(`should never complete`, async () => {
                let completed = false;
                const wrapper = async () => {
                    await PromisePal.never;
                    completed = true;
                };

                await Promise.race([PromisePal.delay(100), wrapper()]);
                expect(completed).to.equal(false);
            });

            describe(`📞 "catch" method`, () => {
                it(`should not throw`, () => {
                    const act = () => PromisePal.never.catch(reason => {});

                    expect(act).not.to.throw();
                });

                it(`should return ${PromisePal.name}.never`, () => {
                    expect(PromisePal.never.catch(reason => {})).to.equal(PromisePal.never);
                });
            });

            describe(`📞 "finally" method`, () => {
                it(`should not throw`, () => {
                    const act = () => PromisePal.never.finally(() => {});

                    expect(act).not.to.throw();
                });

                it(`should return ${PromisePal.name}.never`, () => {
                    expect(PromisePal.never.finally(() => {})).to.equal(PromisePal.never);
                });
            });
        });
    });

    describe(`📞 "delay" static method`, () => {
        describe(`should throw for invalid args`, () => {
            const argsList = [
                [],
                ['foo'],
                [{ foo: 'bar', frob: 123, x: true }],
                [TimeSpan.fromMinutes(-2)],
                [-200],
            ] as any as Parameters<typeof PromisePal.delay>[];

            for (const args of argsList) {
                it(`when args === ${JSON.stringify(args)}`, () => {
                    const act = () => PromisePal.delay(...args);
                    expect(act).to.throw();
                });
            }
        });

        describe(`should not throw for valid args`, () => {
            function theory(...args: Parameters<typeof PromisePal.delay>) {
                it(`when args === ${JSON.stringify(args)}`, () => {});
            }

            theory(TimeSpan.fromSeconds(1));
            theory(TimeSpan.fromMilliseconds(1));
            theory(TimeSpan.zero);
            theory(Timeout.infiniteTimeSpan);

            theory(1000);
            theory(1);
            theory(0);
            theory(-1);
        });

        it(`should return ${PromisePal.name}.never for ${Timeout}.infiniteTimespan`, () => {
            expect(PromisePal.delay(Timeout.infiniteTimeSpan)).to.equal(PromisePal.never);
        });

        describe(`the returned value`, () => {
            it(`should be a ${Promise.name}`, () => {
                expect(PromisePal.delay(1)).to.be.instanceOf(Promise);
            });

            it(`should complete after the specified time period`, async () => {
                const promise = PromisePal.delay(10);

                let completed = false;
                async function follow(): Promise<void> {
                    try {
                        await promise;
                    } finally {
                        completed = true;
                    }
                }

                follow();

                await new Promise<void>(resolve => setTimeout(resolve, 2));
                expect(completed).to.equal(false);

                await new Promise<void>(resolve => setTimeout(resolve, 100));
                expect(completed).to.equal(true);
            });

            it(`should be cancellable through the passed in CT`, async () => {
                const cts = new CancellationTokenSource();
                try {
                    const promise = PromisePal.delay(10, cts.token);

                    let completed = false;
                    let caught: any = undefined;
                    async function follow(): Promise<void> {
                        try {
                            await promise;
                        } catch (e) {
                            caught = e;
                        } finally {
                            completed = true;
                        }
                    }

                    follow();

                    await new Promise<void>(resolve => setTimeout(resolve, 2));
                    expect(completed).to.equal(false);

                    cts.cancel();

                    try {
                        await promise;
                    } catch {}

                    expect(caught).to.be.instanceOf(OperationCanceledError);
                } finally {
                    cts.dispose();
                }
            });

            it(`should complete successfully when the passed CT never requests cancellation`, async () => {
                const cts = new CancellationTokenSource();
                let completedSuccessfully = false;

                try {
                    await PromisePal.delay(10, cts.token);
                    completedSuccessfully = true;
                } finally {
                    cts.dispose();
                }

                expect(completedSuccessfully).to.equal(true);
            });
        });
    });

    describe(`📞 "spy" static method`, () => {
        it(`should not throw when called with a promise`, () => {
            async function stateMachine(): Promise<void> {}

            const promise = stateMachine();

            const act = () => {
                const _ = PromisePal.spy(promise);
            };
            expect(act).not.to.throw();
        });

        for (const promise of [null as any as Promise<void>, undefined as any as Promise<void>]) {
            it(`should throw when called with a falsy promise`, () => {
                const act = () => {
                    const _ = PromisePal.spy(promise);
                };
                expect(act).to.throw(ArgumentNullError);
            });
        }

        it(`should return a PromiseSpy that successfully tracks the resolution of the promise`, async () => {
            const result = 100;
            const event = new AsyncAutoResetEvent();

            async function machine(): Promise<number> {
                await event.waitOne();
                return result;
            }

            const promise = machine();

            const spy = PromisePal.spy(promise);

            expect(spy.status).to.eq(PromiseStatus.Running);

            await PromisePal.delay(10);

            expect(spy.status).to.eq(PromiseStatus.Running);

            event.set();

            await PromisePal.delay(10);

            expect(spy.status).to.eq(PromiseStatus.Succeeded);
            expect(spy.error).to.be.undefined;
            expect(spy.result).to.eq(result);
        });

        it(`should return a PromiseSpy that successfully tracks the rejection of the promise`, async () => {
            const reason = new Error();
            const event = new AsyncAutoResetEvent();

            async function machine(): Promise<number> {
                await event.waitOne();
                throw reason;
            }

            const promise = machine();

            const spy = PromisePal.spy(promise);

            expect(spy.status).to.eq(PromiseStatus.Running);

            await PromisePal.delay(10);

            expect(spy.status).to.eq(PromiseStatus.Running);

            event.set();

            await PromisePal.delay(10);

            expect(spy.status).to.eq(PromiseStatus.Faulted);
            expect(spy.result).to.be.undefined;
            expect(spy.error).to.eq(reason);
        });

        it(`should return a PromiseSpy that successfully tracks the cancellation of the promise`, async () => {
            const cts = new CancellationTokenSource();
            try {
                async function machine(): Promise<number> {
                    await PromisePal.delay(TimeSpan.fromHours(2), cts.token);
                    return 100;
                }

                const promise = machine();

                const spy = PromisePal.spy(promise);

                expect(spy.status).to.eq(PromiseStatus.Running);

                await PromisePal.delay(10);

                expect(spy.status).to.eq(PromiseStatus.Running);

                cts.cancel();

                await PromisePal.delay(10);

                expect(spy.status).to.eq(PromiseStatus.Canceled);
                expect(spy.result).to.be.undefined;
                expect(spy.error).to.be.instanceOf(OperationCanceledError);
            } finally {
                cts.dispose();
            }
        });
    });
});
