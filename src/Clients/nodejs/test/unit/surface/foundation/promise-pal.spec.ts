import { expect, spy, constructing, toJavaScript, calling } from '@test-helpers';
import { Timeout, TimeSpan, ArgumentOutOfRangeError, ArgumentNullError, CancellationToken, CancellationTokenSource, OperationCanceledError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`Promise extensions`, () => {
        describe(`the Promise.never property`, () => {
            it(`should not throw`, () => {
                (() => Promise.never).should.not.throw();
            });

            it(`should return the same object everytime`, () => {
                expect(Promise.never).to.be.eq(Promise.never);
            });

            it(`should never resolve or reject`, async () => {
                const spyHandler = spy(() => { });
                Promise.never.then(spyHandler);
                Promise.never.catch(spyHandler);
                Promise.never.finally(spyHandler);
                await Promise.delay(TimeSpan.fromSeconds(0.2));
                spyHandler.should.not.have.been.called();
            });

            const emptyHandler = () => { };
            type Method = (...args: any[]) => any;
            for (const _case of [
                { method: Promise.never.then, args: [emptyHandler] as const },
                { method: Promise.never.catch, args: [emptyHandler] as const },
                { method: Promise.never.finally, args: [emptyHandler] as const },
                { method: Promise.never.observe, args: [] as const },
            ]) {
                const strArgs = (_case.args as any as unknown[]).map(toJavaScript).join(', ');

                it(`calling Promise.never.${_case.method.name}(${strArgs}) should not throw`, () => {
                    calling(_case.method, ..._case.args).should.not.throw();
                });

                it(`calling Promise.never.${_case.method.name}(${strArgs}) should return Promise.never`, () => {
                    expect((_case.method as Method)(..._case.args))
                        .to.be.eq(Promise.never);
                });
            }
        });

        describe(`the Promise.delay method`, () => {
            for (const argInterval of [0, 1, TimeSpan.fromMilliseconds(1)]) {
                for (const argsCt of [[], [CancellationToken.none], [new CancellationTokenSource().token]]) {
                    const args = [argInterval, ...argsCt] as any as [TimeSpan, CancellationToken?];

                    it(`calling Promise.delay(${args.map(toJavaScript).join(', ')}) should not throw`, () => {
                        calling(Promise.delay, ...args).should.not.throw();
                    });
                }
            }

            it(`should not throw when called with an infinite interval`, () => {
                calling(Promise.delay, Timeout.infiniteTimeSpan).should.not.throw();
                calling(Promise.delay, -1 as never).should.not.throw();
            });

            it(`should throw when called with a negative interval which isn't infinity`, () => {
                calling(Promise.delay, TimeSpan.fromMilliseconds(-100))
                    .should.throw(
                        ArgumentOutOfRangeError,
                        'Specified argument represented a negative interval.')
                    .with.property('paramName', 'delay');

                calling(Promise.delay, -100 as never)
                    .should.throw(
                        ArgumentOutOfRangeError,
                        'Specified argument represented a negative interval.')
                    .with.property('paramName', 'millisecondsDelay');
            });

            context(`should throw when called with something other than a number or a TimeSpan`, () => {
                for (const arg of [true, () => { }, {}, Symbol()] as never[]) {
                    it(`Promise.delay(${toJavaScript(arg)}) should throw`, () => {
                        calling(Promise.delay, arg)
                            .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: 'number', TimeSpan.`)
                            .with.property('paramName', 'arg0');
                    });
                }
            });

            it(`should throw when called with zero args`, () => {
                const emptyArgs = [] as unknown as [TimeSpan];
                calling(Promise.delay, ...emptyArgs)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'arg0');
            });

            it(`should return Promise.never when called with an infinite interval`, () => {
                expect(Promise.delay(Timeout.infiniteTimeSpan)).to.be.eq(Promise.never);
                expect(Promise.delay(-1)).to.be.eq(Promise.never);
            });

            it(`should return a Promise which resolves after the specified interval`, async () => {
                const spyThen = spy(() => { });
                Promise.delay(1).then(spyThen);
                await new Promise(resolve => setTimeout(resolve, 100));
                spyThen.should.have.been.called();
            });

            it(`should return a Promise which rejects as soon as the specified CancellationToken is triggered, if that happens before the specified interval passes`, async () => {
                const spyCatch = spy((error: Error) => {
                    expect(error).to.be.instanceOf(OperationCanceledError);
                });
                const cts = new CancellationTokenSource();
                Promise.delay(TimeSpan.fromHours(1), cts.token).catch(spyCatch);
                cts.cancel();
                await new Promise(resolve => setTimeout(resolve, 0));
                spyCatch.should.have.been.called();
            });
        });
    });
});
