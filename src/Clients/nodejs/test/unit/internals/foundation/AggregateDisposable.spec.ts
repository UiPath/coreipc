import { spy, constructing, forInstance } from '@test-helpers';
import { AggregateDisposable, AggregateError, ArgumentError } from '@foundation';

describe(`internals`, () => {
    describe(`AggregateDisposable`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                constructing(AggregateDisposable, {
                    dispose() { },
                }).should.not.throw();
            });

            it(`should throw when called with zero disposables`, () => {
                constructing(AggregateDisposable)
                    .should.throw(ArgumentError, 'No disposables were supplied.');
            });
        });

        context(`the dispose method`, () => {
            it(`should not throw`, () => {
                const ad = new AggregateDisposable({
                    dispose() { },
                });

                forInstance(ad).calling('dispose')
                    .should.not.throw();
            });

            it(`should call the contained disposables' dispose method`, () => {
                const spiedDispose = spy(() => { });

                const ad = new AggregateDisposable({
                    dispose: spiedDispose,
                });

                ad.dispose();
                spiedDispose.should.have.been.called();
            });

            it(`should throw AggregateError with all the errors thrown by the contained disposables`, () => {
                const errors = Array.from(Array(3).keys()).map(() => new Error());
                const disposables = errors.map(error => ({
                    dispose() {
                        throw error;
                    },
                }));

                const ad = new AggregateDisposable(...disposables);
                forInstance(ad).calling('dispose')
                    .should.throw(AggregateError)
                    .with.property('errors')
                    .which.contains.members(errors);
            });
        });
    });
});
