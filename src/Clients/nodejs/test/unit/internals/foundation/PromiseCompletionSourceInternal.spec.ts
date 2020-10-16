import { expect, spy, constructing, calling, forInstance, toJavaScript } from '@test-helpers';
import {
    PromiseCompletionSourceInternal,
    FinalState,
    ArgumentOutOfRangeError,
    ArgumentNullError,
    InvalidOperationError,
} from '@foundation';

describe(`internals`, () => {
    describe(`PromiseCompletionSourceInternal`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                constructing(PromiseCompletionSourceInternal).should.not.throw();
            });
        });

        context(`the promise property`, () => {
            it(`should not throw when read`, () => {
                const pcs = new PromiseCompletionSourceInternal();
                (() => pcs.promise).should.not.throw();
            });

            it(`should be a Promise`, () => {
                const pcs = new PromiseCompletionSourceInternal();
                expect(pcs.promise).to.be.instanceOf(Promise);
            });
        });

        context(`the trySetFinalState method`, () => {
            context(`it should not throw when called the 1st time with valid args`, () => {
                for (const finalState of [
                    FinalState.ranToCompletion(123),
                    FinalState.faulted(new Error('error message')),
                    FinalState.canceled(),
                ]) {
                    it(`trySetFinalState(${toJavaScript(finalState)})`, () => {
                        const pcs = new PromiseCompletionSourceInternal();
                        forInstance(pcs)
                            .calling('trySetFinalState', finalState)
                            .should.not.throw();
                    });
                }
            });

            it(`it should throw when called with invalid args`, () => {
                function forNewPcs() { return forInstance(new PromiseCompletionSourceInternal<number>()); }

                forNewPcs().calling('trySetFinalState', null as never)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'finalState');

                forNewPcs().calling('trySetFinalState', undefined as never)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'finalState');

                forNewPcs().calling('trySetFinalState', {} as never)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: FinalStateRanToCompletion, FinalStateFaulted, FinalStateCanceled.`)
                    .with.property('paramName', 'finalState');
            });

            context(`it should not throw when called a 2nd time`, () => {
                for (const finalState of [
                    FinalState.ranToCompletion(123),
                    FinalState.faulted(new Error('error message')),
                    FinalState.canceled(),
                ]) {
                    it(`trySetFinalState(${toJavaScript(finalState)})`, () => {
                        const pcs = new PromiseCompletionSourceInternal();
                        pcs.trySetFinalState(FinalState.ranToCompletion(123));

                        forInstance(pcs)
                            .calling('trySetFinalState', finalState)
                            .should.not.throw();
                    });
                }
            });

            context(`it should return true when called the 1st time`, () => {
                for (const finalState of [
                    FinalState.ranToCompletion(123),
                    FinalState.faulted(new Error('error message')),
                    FinalState.canceled(),
                ]) {
                    it(`trySetFinalState(${toJavaScript(finalState)})`, () => {
                        const pcs = new PromiseCompletionSourceInternal();
                        expect(pcs.trySetFinalState(finalState)).to.be.eq(true);
                    });
                }
            });

            context(`it should return false when called a 2nd time`, () => {
                for (const finalState of [
                    FinalState.ranToCompletion(123),
                    FinalState.faulted(new Error('error message')),
                    FinalState.canceled(),
                ]) {
                    it(`trySetFinalState(${toJavaScript(finalState)})`, () => {
                        const pcs = new PromiseCompletionSourceInternal();
                        pcs.trySetFinalState(FinalState.ranToCompletion(123));
                        expect(pcs.trySetFinalState(finalState)).to.be.eq(false);
                    });
                }
            });

            context(`it should eventually transition the associated promise to the final state`, () => {
                it(`it should eventually resolve the promise when called with a FinalState of kind RanToCompletion`, async () => {
                    const finalState = FinalState.ranToCompletion(123);
                    const pcsi = new PromiseCompletionSourceInternal();
                    const spyHandler = spy(() => { });
                    pcsi.promise.then(spyHandler);
                    pcsi.trySetFinalState(finalState);
                    await Promise.yield();
                    spyHandler.should.have.been.called.with(123);
                });

                it(`it should eventually reject the promise when called with a FinalState of kind Faulted`, async () => {
                    const error = new Error();
                    const finalState = FinalState.faulted(error);
                    const pcsi = new PromiseCompletionSourceInternal();
                    const spyHandler = spy(() => { });
                    pcsi.promise.catch(spyHandler);
                    pcsi.trySetFinalState(finalState);
                    await Promise.yield();
                    spyHandler.should.have.been.called.with(error);
                });
            });
        });

        context(`the setFinalState method`, () => {
            context(`it should not throw when called the 1st time with valid args`, () => {
                for (const finalState of [
                    FinalState.ranToCompletion(123),
                    FinalState.faulted(new Error('error message')),
                    FinalState.canceled(),
                ]) {
                    it(`setFinalState(${toJavaScript(finalState)})`, () => {
                        const pcs = new PromiseCompletionSourceInternal();
                        forInstance(pcs)
                            .calling('setFinalState', finalState)
                            .should.not.throw();
                    });
                }
            });

            it(`it should throw when called with invalid args`, () => {
                function forNewPcs() { return forInstance(new PromiseCompletionSourceInternal<number>()); }

                forNewPcs().calling('setFinalState', null as never)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'finalState');

                forNewPcs().calling('setFinalState', undefined as never)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'finalState');

                forNewPcs().calling('setFinalState', {} as never)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument's type was neither of: FinalStateRanToCompletion, FinalStateFaulted, FinalStateCanceled.`)
                    .with.property('paramName', 'finalState');
            });

            context(`it should throw when called a 2nd time`, () => {
                for (const finalState of [
                    FinalState.ranToCompletion(123),
                    FinalState.faulted(new Error('error message')),
                    FinalState.canceled(),
                ]) {
                    it(`setFinalState(${toJavaScript(finalState)})`, () => {
                        const pcs = new PromiseCompletionSourceInternal();
                        pcs.setFinalState(FinalState.ranToCompletion(123));

                        forInstance(pcs)
                            .calling('setFinalState', finalState)
                            .should.throw(InvalidOperationError, 'An attempt was made to transition a task to a final state when it had already completed.');
                    });
                }
            });
        });
    });
});
