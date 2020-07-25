import { spy, expect, constructing, calling } from '@test-helpers';
import { CancellationTokenSource, ArgumentOutOfRangeError, TimeSpan, Timeout, CancellationToken, ObjectDisposedError, ArgumentError, AggregateError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`CancellationTokenSource`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                constructing(CancellationTokenSource, undefined as never).should.not.throw();

                constructing(CancellationTokenSource, -1 as never).should.not.throw();
                constructing(CancellationTokenSource, +0 as never).should.not.throw();
                constructing(CancellationTokenSource, +1 as never).should.not.throw();

                constructing(CancellationTokenSource, Timeout.infiniteTimeSpan).should.not.throw();
                constructing(CancellationTokenSource, TimeSpan.zero).should.not.throw();
                constructing(CancellationTokenSource, TimeSpan.fromMilliseconds(1)).should.not.throw();
            });

            it(`should throw when called with something other than a 'number' or a TimeSpan`, () => {
                for (const outOfRangeArg of ['some string', true, Symbol(), () => { }] as never[]) {
                    constructing(CancellationTokenSource, outOfRangeArg)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: 'number', TimeSpan.`)
                        .with.property('paramName', 'arg0');
                }
            });

            it(`should throw when called with a negative interval which isn't -1 milliseconds which encodes infinity`, () => {
                const cases = [
                    { arg: -2 as never, paramName: 'millisecondsDelay' },
                    { arg: -3 as never, paramName: 'millisecondsDelay' },
                    { arg: TimeSpan.fromMilliseconds(-2) as never, paramName: 'delay' },
                    { arg: TimeSpan.fromMilliseconds(-3) as never, paramName: 'delay' },
                ];
                for (const _case of cases) {
                    constructing(CancellationTokenSource, _case.arg)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument represented a negative interval.`)
                        .with.property('paramName', _case.paramName);
                }
            });
        });

        context(`the dispose method`, () => {
            it(`should not throw (even if called multiple times)`, () => {
                const cts = new CancellationTokenSource();

                calling(cts.dispose.bind(cts)).should.not.throw();
                calling(cts.dispose.bind(cts)).should.not.throw();
            });
        });

        context(`the token property`, () => {
            it(`should not throw`, () => {
                const cts = new CancellationTokenSource();

                calling(() => cts.token).should.not.throw();
            });

            it(`should return a CancellationToken`, () => {
                const cts = new CancellationTokenSource();

                cts.token.should.be.instanceOf(CancellationToken);
            });
        });

        context(`the cancel method`, () => {
            it(`should not throw when no callbacks registered on the associated CancellationToken are throwing`, () => {
                function bind() {
                    const cts = new CancellationTokenSource();
                    return cts.cancel.bind(cts);
                }

                for (const args of [
                    [],
                    [false],
                    [true],
                ] as never[][]) {
                    calling(bind(), ...args).should.not.throw();
                }
            });

            it(`should throw even if no callbacks registered on the associated CancellationToken are throwing, if called with an arg which isn't a boolean`, () => {
                function bind() {
                    const cts = new CancellationTokenSource();
                    return cts.cancel.bind(cts);
                }

                for (const nonBoolArg of [123, 'some string', Symbol()] as never[]) {
                    calling(bind(), nonBoolArg)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument was not of type 'boolean'.`)
                        .with.property('paramName', 'throwOnFirstError');
                }
            });

            it(`should trigger the associated CancellationToken`, () => {
                for (const throwOnFirstError of [undefined, false, true]) {
                    const cts = new CancellationTokenSource();

                    cts.token.isCancellationRequested.should.be.eq(false);
                    cts.cancel(throwOnFirstError);
                    cts.token.isCancellationRequested.should.be.eq(true);
                }
            });

            it(`should call callbacks registered on the associated CancellationToken exactly once`, () => {
                const cts = new CancellationTokenSource();
                const spyHandler = spy(() => { });
                cts.token.register(spyHandler);

                cts.cancel();
                let _ = spyHandler.should.have.been.called();

                cts.cancel();
                _ = spyHandler.should.have.been.called.once;
            });

            it(`should throw the first error any callback throws when called with throwOnFirstError === true`, () => {
                const cts = new CancellationTokenSource();

                const errors = Array.from(Array(5).keys()).map(_ => new Error());
                const nonThrowingCallbacks = Array.from(Array(5).keys()).map(_ => spy(() => { }));
                const throwingCallbacks = errors.map(error => spy(() => { throw error; }));
                const callbacks = [...nonThrowingCallbacks, ...throwingCallbacks];
                callbacks.map(cts.token.register.bind(cts.token));

                callbacks.forEach(callback => callback.should.not.have.been.called());
                calling(cts.cancel.bind(cts), true).should.throw(Error).which.is.eq(errors[0]);

                nonThrowingCallbacks.forEach(callback => callback.should.have.been.called());
                throwingCallbacks[0].should.have.been.called();
                throwingCallbacks.slice(1).forEach(callback => callback.should.not.have.called());
            });

            it(`should throw an AggregateError with all the errors thrown by the callbacks when called with throwOnFirstError === false`, () => {
                for (const args of [
                    [],
                    [false],
                ] as never[][]) {
                    const cts = new CancellationTokenSource();

                    const errors = Array.from(Array(5).keys()).map(_ => new Error());
                    const nonThrowingCallbacks = Array.from(Array(5).keys()).map(_ => spy(() => { }));
                    const throwingCallbacks = errors.map(error => spy(() => { throw error; }));
                    const callbacks = [...nonThrowingCallbacks, ...throwingCallbacks];
                    callbacks.map(cts.token.register.bind(cts.token));

                    callbacks.forEach(callback => callback.should.not.have.been.called());

                    calling(cts.cancel.bind(cts), ...args)
                        .should.throw(AggregateError)
                        .with.property('errors')
                        .which.is.deep.eq(errors);

                    callbacks.forEach(callback => callback.should.have.been.called());
                }
            });
        });

        context(`the cancelAfter method`, () => {
            it(`should not throw`, () => {
                calling(bind(), -1 as never).should.not.throw();
                calling(bind(), +0 as never).should.not.throw();
                calling(bind(), +1 as never).should.not.throw();

                calling(bind(), Timeout.infiniteTimeSpan).should.not.throw();
                calling(bind(), TimeSpan.zero).should.not.throw();
                calling(bind(), TimeSpan.fromMilliseconds(1)).should.not.throw();
            });

            it(`should throw when called with something other than a number of a TimeSpan`, () => {
                for (const outOfRangeArg of ['some string', true, Symbol(), () => { }, {}] as never[]) {
                    calling(bind(), outOfRangeArg)
                        .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: 'number', TimeSpan.`)
                        .with.property('paramName', 'arg0');
                }
            });

            it(`should throw when called on a disposed CancellationTokenSource`, () => {
                const cts = new CancellationTokenSource();
                cts.dispose();
                calling(cts.cancelAfter.bind(cts), TimeSpan.zero).should.throw(ObjectDisposedError);
            });

            it(`should throw when called with negative non-infinite intervals`, () => {
                calling(bind(), -2 as never)
                    .should.throw(ArgumentOutOfRangeError)
                    .with.property('paramName', 'millisecondsDelay');

                calling(bind(), TimeSpan.fromMilliseconds(-2))
                    .should.throw(ArgumentOutOfRangeError)
                    .with.property('paramName', 'delay');
            });

            it(`should eventually trigger the associated CancellationToken`, async () => {
                const cts = new CancellationTokenSource();
                cts.token.isCancellationRequested.should.be.eq(false);
                cts.cancelAfter(TimeSpan.zero);
                await Promise.yield();
                cts.token.isCancellationRequested.should.be.eq(true);
            });

            it(`should not throw when called multiple times`, () => {
                const cts = new CancellationTokenSource();
                cts.cancelAfter(TimeSpan.fromMilliseconds(1));
                calling(cts.cancelAfter.bind(cts), TimeSpan.fromMilliseconds(1)).should.not.throw();
            });

            function bind() {
                const cts = new CancellationTokenSource();
                return cts.cancelAfter.bind(cts);
            }
        });

        context(`the createLinkedTokenSource method`, () => {
            it(`should not throw`, () => {
                calling(CancellationTokenSource.createLinkedTokenSource, CancellationToken.none)
                    .should.not.throw();
            });

            it(`should throw when called with zero args`, () => {
                calling(CancellationTokenSource.createLinkedTokenSource)
                    .should.throw(ArgumentError, 'No tokens were supplied.');
            });

            it(`should throw when called with args that are not CancellationToken instances`, () => {
                for (const args of [
                    [null],
                    [undefined],
                    [123],
                    ['some string'],
                    [true, 123, 'some string'],
                ] as never[][]) {
                    calling(CancellationTokenSource.createLinkedTokenSource, ...args)
                        .should.throw(ArgumentError, 'Some supplied arguments were not instances of CancellationToken.');
                }
            });

            it(`should return an instance of CancellationTokenSource`, () => {
                expect(CancellationTokenSource.createLinkedTokenSource(CancellationToken.none))
                    .to.be.instanceOf(CancellationTokenSource);
            });

            context(`the returned CancellationTokenSource`, () => {
                it(`should trigger it's associated CancellationToken when any of the original tokens used in creating it is triggered`, () => {
                    const cts1 = new CancellationTokenSource();
                    const cts2 = new CancellationTokenSource();

                    const cts3 = CancellationTokenSource.createLinkedTokenSource(cts1.token, cts2.token);
                    const spyCallback = spy(() => { });
                    cts3.token.register(spyCallback);

                    spyCallback.should.not.have.been.called();
                    cts3.token.isCancellationRequested.should.be.eq(false);

                    cts1.cancel();

                    spyCallback.should.have.been.called();
                    cts3.token.isCancellationRequested.should.be.eq(true);
                });

                it(`should cause its associated CancellationToken to never become triggered when disposed`, () => {
                    const cts1 = new CancellationTokenSource();
                    const cts2 = new CancellationTokenSource();
                    const cts3 = CancellationTokenSource.createLinkedTokenSource(cts1.token, cts2.token);

                    const spyCallback = spy(() => { });
                    cts3.token.register(spyCallback);
                    cts3.dispose();

                    cts1.cancel();
                    spyCallback.should.not.have.been.called();
                });

                it(`should not throw if disposed a 2nd time`, () => {
                    const linkedSource = CancellationTokenSource.createLinkedTokenSource(CancellationToken.none);
                    linkedSource.dispose();
                    calling(linkedSource.dispose.bind(linkedSource)).should.not.throw();
                });
            });
        });
    });
});
