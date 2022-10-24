import { expect, spy, constructing, calling } from '@test-helpers';
import { CancellationToken, ArgumentNullError, CancellationTokenSource, OperationCanceledError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`CancellationToken`, () => {
        context(`the none property`, () => {
            it(`should not throw`, () => {
                (() => CancellationToken.none).should.not.throw();
            });

            it(`should return a CancellationToken`, () => {
                CancellationToken.none.should.be.instanceOf(CancellationToken);
            });

            it(`should return a CancellationToken which cannot be canceled`, () => {
                CancellationToken.none.canBeCanceled.should.be.eq(false);
            });
        });

        context(`the register method`, () => {
            it(`should not throw`, () => {
                calling(CancellationToken.none.register, () => { }).should.not.throw();
            });

            it(`should throw when called with a falsy callback`, () => {
                for (const falsyCallback of [null, undefined] as never[]) {
                    calling(CancellationToken.none.register, falsyCallback)
                        .should.throw(ArgumentNullError)
                        .with.property('paramName', 'callback');
                }
            });

            it(`should return an IDisposable`, () => {
                for (const ct of enumerateCancellationTokens()) {
                    const rv = ct.register(() => { });

                    expect(rv).to.satisfy((x: any) => {
                        expect(x).to.be.an('object');
                        expect(x.dispose).to.be.a('function');
                        return true;
                    });
                }
            });

            it(`should register the callback so that it's called when the CancellationToken is triggered`, () => {
                const cts = new CancellationTokenSource();
                const spyHandler = spy(() => { });
                cts.token.register(spyHandler);
                spyHandler.should.not.have.been.called();
                cts.cancel();
                spyHandler.should.have.been.called();
            });

            it(`should cause the callback to be called inline when the CancellationToken is already triggered`, () => {
                const cts = new CancellationTokenSource();
                cts.cancel();

                const spyHandler = spy(() => { });
                cts.token.register(spyHandler);
                spyHandler.should.have.been.called();
            });

            context(`the returned IDisposable`, () => {
                context(`the dispose method`, () => {
                    it(`should not throw even if called multiple times`, () => {
                        for (const ct of enumerateCancellationTokens()) {
                            const disposable = ct.register(() => { });
                            calling(disposable.dispose.bind(disposable))
                                .should.not.throw();
                        }
                    });

                    it(`should unregister the callback so that it's not called when the CancellationToken is triggered`, () => {
                        const cts = new CancellationTokenSource();
                        const spyCallback = spy(() => { });
                        const reg = cts.token.register(spyCallback);
                        reg.dispose();
                        cts.cancel();
                        spyCallback.should.not.have.been.called();
                    });
                });
            });
        });

        context(`the canBeCanceled property`, () => {
            it(`should not throw`, () => {
                for (const ct of enumerateCancellationTokens()) {
                    (() => { const _ = ct.canBeCanceled; })
                        .should.not.throw();
                }
            });

            it(`should be false for CancellationToken.none`, () => {
                CancellationToken.none.canBeCanceled.should.be.eq(false);
            });

            it(`should be true for other CancellationToken instances`, () => {
                new CancellationTokenSource().token.canBeCanceled.should.be.eq(true);
            });
        });

        context(`the isCancellationRequested property`, () => {
            it(`should not throw`, () => {
                for (const ct of enumerateCancellationTokens()) {
                    (() => { const _ = ct.isCancellationRequested; })
                        .should.not.throw();
                }
            });

            it(`should be false initially`, () => {
                it(`should not throw`, () => {
                    for (const ct of enumerateCancellationTokens()) {
                        ct.isCancellationRequested.should.be.eq(false);
                    }
                });
            });

            it(`should become true when the CancellationToken is canceled`, () => {
                const cts = new CancellationTokenSource();
                cts.cancel();
                cts.token.isCancellationRequested.should.be.eq(true);
            });
        });

        context(`the throwIfCancellationRequested method`, () => {
            it(`should not throw initially`, () => {
                for (const ct of enumerateCancellationTokens()) {
                    calling(ct.throwIfCancellationRequested.bind(ct))
                        .should.not.throw();
                }
            });

            it(`should throw if called after the CancellationToken was canceled`, () => {
                const cts = new CancellationTokenSource();
                cts.cancel();
                calling(cts.token.throwIfCancellationRequested.bind(cts.token))
                    .should.throw(OperationCanceledError);
            });
        });

        function* enumerateCancellationTokens() {
            yield CancellationToken.none;
            yield new CancellationTokenSource().token;
        }
    });
});
