import { expect, spy, constructing, forInstance } from '@test-helpers';

import {
    PromiseCompletionSource,
    FinalState,
    ArgumentOutOfRangeError,
    PromiseCompletionSourceInternal,
} from '@foundation';

describe(`surface:foundation`, () => {
    describe(`PromiseCompletionSource`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                constructing(PromiseCompletionSource).should.not.throw();

                constructing(PromiseCompletionSource, {
                    promise: Promise.never,
                    setFinalState: _state => { },
                    trySetFinalState: _state => false,
                }).should.not.throw();
            });

            it(`should throw when called with something which isn't an object`, () => {
                for (const outOfRangeArg of [123, true, Symbol(), () => { }, 'some string'] as never[]) {
                    constructing(PromiseCompletionSource, outOfRangeArg)
                        .should.throw(ArgumentOutOfRangeError)
                        .with.property('paramName', 'internal');
                }
            });
        });

        context(`the promise property`, () => {
            it(`should return the underlying PromiseCompletionSourceInternal's promise`, () => {
                const internal = new PromiseCompletionSourceInternal();
                const spyHandler = spy(() => internal.promise);

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    get promise(): Promise<unknown> { return spyHandler(); },
                } as never);

                expect(pcs.promise).to.be.eq(internal.promise);
                spyHandler.should.have.been.called();
            });
        });

        context(`the setFinalState method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's setFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('setFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    setFinalState: underlyingMethod,
                });

                const markedFinalState = FinalState.ranToCompletion(123);

                pcs.setFinalState(markedFinalState);
                underlyingMethod.should.have.been.called.with(markedFinalState);
            });
        });

        context(`the setResult method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's setFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('setFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    setFinalState: underlyingMethod,
                });

                pcs.setResult(123);
                underlyingMethod.should.have.been.called.with(FinalState.ranToCompletion(123));
            });
        });

        context(`the setFaulted method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's setFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('setFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    setFinalState: underlyingMethod,
                });

                const someError = new Error('some error');
                pcs.setFaulted(someError);
                underlyingMethod.should.have.been.called.with(FinalState.faulted(someError));
            });
        });

        context(`the setCanceled method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's setFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('setFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    setFinalState: underlyingMethod,
                });

                pcs.setCanceled();
                underlyingMethod.should.have.been.called.with(FinalState.canceled());
            });
        });

        context(`the trySetFinalState method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's trySetFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('trySetFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    trySetFinalState: underlyingMethod,
                });

                const markedFinalState = FinalState.ranToCompletion(123);

                pcs.trySetFinalState(markedFinalState);
                underlyingMethod.should.have.been.called.with(markedFinalState);
            });
        });

        context(`the trySetResult method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's trySetFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('trySetFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    trySetFinalState: underlyingMethod,
                });

                pcs.trySetResult(123);
                underlyingMethod.should.have.been.called.with(FinalState.ranToCompletion(123));
            });
        });

        context(`the trySetFaulted method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's trySetFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('trySetFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    trySetFinalState: underlyingMethod,
                });

                const someError = new Error('some error');
                pcs.trySetFaulted(someError);
                underlyingMethod.should.have.been.called.with(FinalState.faulted(someError));
            });
        });

        context(`the trySetCanceled method`, () => {
            it(`should call the underlying PromiseCompletionSourceInternal's trySetFinalState`, () => {
                const internal = PromiseCompletionSourceInternal.create();
                const underlyingMethod = forInstance(internal).spyOn('trySetFinalState');

                const pcs = new PromiseCompletionSource({
                    ...internal,
                    trySetFinalState: underlyingMethod,
                });

                pcs.trySetCanceled();
                underlyingMethod.should.have.been.called.with(FinalState.canceled());
            });
        });
    });
});
