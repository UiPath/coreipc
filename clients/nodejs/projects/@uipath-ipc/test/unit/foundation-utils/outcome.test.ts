// tslint:disable: no-unused-expression

import { spy, use } from 'chai';
import spies from 'chai-spies';

import { Succeeded, Faulted, Canceled, OutcomeKind } from '@foundation/utils';
import { ArgumentNullError, OperationCanceledError } from '@foundation/errors';
import { PromiseCompletionSource } from '@foundation/threading';

use(spies);

describe(`foundation:utils -> hierarchy:Outcome`, () => {
    describe(`class:Succeeded`, () => {
        context(`ctor`, () => {
            it(`shouldn't throw`, () => {
                (() => new Succeeded<string>('foo')).should.not.throw();
            });
        });

        context(`method:isSucceeded`, () => {
            it(`shouldn't throw`, () => {
                (() => new Succeeded<string>('foo').isSucceeded()).should.not.throw();
            });

            it(`should return true`, () => {
                new Succeeded<string>('foo').isSucceeded().should.be.true;
            });
        });

        context(`method:isFaulted`, () => {
            it(`shouldn't throw`, () => {
                (() => new Succeeded<string>('foo').isFaulted()).should.not.throw();
            });

            it(`should return false`, () => {
                new Succeeded<string>('foo').isFaulted().should.be.false;
            });
        });

        context(`method:isCanceled`, () => {
            it(`shouldn't throw`, () => {
                (() => new Succeeded<string>('foo').isSucceeded()).should.not.throw();
            });

            it(`should return false`, () => {
                new Succeeded<string>('foo').isCanceled().should.be.false;
            });
        });

        context(`field:kind`, () => {
            it(`should equal OutcomeKind.Succeeded`, () => {
                new Succeeded<string>('foo').kind.should.be.equal(OutcomeKind.Succeeded);
            });
        });

        context(`method:apply`, () => {
            it(`should throw ArgumentNullError provided a falsy pcs`, () => {
                (() => new Succeeded<string>('foo').apply(null as any)).
                    should.throw(ArgumentNullError).
                    with.property('paramName', 'pcs');
            });

            it(`should call setResult on the provided pcs`, () => {
                const mock: PromiseCompletionSource<string> = {
                    setResult: spy(() => { })
                } as any;
                new Succeeded<string>('foo').apply(mock);
                mock.setResult.should.have.been.called.with('foo');
            });
        });

        context(`method:tryApply`, () => {
            it(`should throw ArgumentNullError provided a falsy pcs`, () => {
                (() => new Succeeded<string>('foo').tryApply(null as any)).
                    should.throw(ArgumentNullError).
                    with.property('paramName', 'pcs');
            });

            it(`should call trySetResult on the provided pcs`, () => {
                const mock: PromiseCompletionSource<string> = {
                    trySetResult: spy(() => { })
                } as any;
                new Succeeded<string>('foo').tryApply(mock);
                mock.trySetResult.should.have.been.called.with('foo');
            });
        });

        context(`property:result`, () => {
            it(`shouldn't throw`, () => {
                (() => new Succeeded<string>('foo')).should.not.throw();
            });

            it(`should return the inner value`, () => {
                new Succeeded<string>('foo').result.should.be.equal('foo');
            });
        });
    });

    describe(`class:Faulted`, () => {
        const error = new Error();

        context(`ctor`, () => {
            it(`shouldn't throw`, () => {
                (() => new Faulted<string>(error)).should.not.throw();
            });
        });

        context(`method:isSucceeded`, () => {
            it(`shouldn't throw`, () => {
                (() => new Faulted<string>(error).isSucceeded()).should.not.throw();
            });

            it(`should return false`, () => {
                new Faulted<string>(error).isSucceeded().should.be.false;
            });
        });

        context(`method:isFaulted`, () => {
            it(`shouldn't throw`, () => {
                (() => new Faulted<string>(error).isFaulted()).should.not.throw();
            });

            it(`should return true`, () => {
                new Faulted<string>(error).isFaulted().should.be.true;
            });
        });

        context(`method:isCanceled`, () => {
            it(`shouldn't throw`, () => {
                (() => new Faulted<string>(error).isCanceled()).should.not.throw();
            });

            it(`should return false`, () => {
                new Faulted<string>(error).isCanceled().should.be.false;
            });
        });

        context(`field:kind`, () => {
            it(`should equal OutcomeKind.Faulted`, () => {
                new Faulted<string>(error).kind.should.be.equal(OutcomeKind.Faulted);
            });
        });

        context(`method:apply`, () => {
            it(`should throw ArgumentNullError provided a falsy pcs`, () => {
                (() => new Faulted<string>(error).apply(null as any)).
                    should.throw(ArgumentNullError).
                    with.property('paramName', 'pcs');
            });

            it(`should call setError on the provided pcs`, () => {
                const mock: PromiseCompletionSource<string> = {
                    setError: spy(() => { })
                } as any;
                new Faulted<string>(error).apply(mock);
                mock.setError.should.have.been.called.with(error);
            });
        });

        context(`method:tryApply`, () => {
            it(`should throw ArgumentNullError provided a falsy pcs`, () => {
                (() => new Faulted<string>(error).tryApply(null as any)).
                    should.throw(ArgumentNullError).
                    with.property('paramName', 'pcs');
            });

            it(`should call trySetError on the provided pcs`, () => {
                const mock: PromiseCompletionSource<string> = {
                    trySetError: spy(() => { })
                } as any;
                new Faulted<string>(error).tryApply(mock);
                mock.trySetError.should.have.been.called.with(error);
            });
        });

        context(`property:result`, () => {
            it(`should throw the inner error`, () => {
                (() => new Faulted<string>(error).result).
                    should.throw(Error).
                    which.satisfies((x: Error) => x === error);
            });
        });
    });

    describe(`class:Canceled`, () => {
        context(`ctor`, () => {
            it(`shouldn't throw`, () => {
                (() => new Canceled<string>()).should.not.throw();
            });
        });

        context(`method:isSucceeded`, () => {
            it(`shouldn't throw`, () => {
                (() => new Canceled<string>().isSucceeded()).should.not.throw();
            });

            it(`should return false`, () => {
                new Canceled<string>().isSucceeded().should.be.false;
            });
        });

        context(`method:isFaulted`, () => {
            it(`shouldn't throw`, () => {
                (() => new Canceled<string>().isFaulted()).should.not.throw();
            });

            it(`should return false`, () => {
                new Canceled<string>().isFaulted().should.be.false;
            });
        });

        context(`method:isCanceled`, () => {
            it(`shouldn't throw`, () => {
                (() => new Canceled<string>().isCanceled()).should.not.throw();
            });

            it(`should return true`, () => {
                new Canceled<string>().isCanceled().should.be.true;
            });
        });

        context(`field:kind`, () => {
            it(`should equal OutcomeKind.Canceled`, () => {
                new Canceled<string>().kind.should.be.equal(OutcomeKind.Canceled);
            });
        });

        context(`method:apply`, () => {
            it(`should throw ArgumentNullError provided a falsy pcs`, () => {
                (() => new Canceled<string>().apply(null as any)).
                    should.throw(ArgumentNullError).
                    with.property('paramName', 'pcs');
            });

            it(`should call setCanceled on the provided pcs`, () => {
                const mock: PromiseCompletionSource<string> = {
                    setCanceled: spy(() => { })
                } as any;
                new Canceled<string>().apply(mock);
                mock.setCanceled.should.have.been.called();
            });
        });

        context(`method:tryApply`, () => {
            it(`should throw ArgumentNullError provided a falsy pcs`, () => {
                (() => new Canceled<string>().tryApply(null as any)).
                    should.throw(ArgumentNullError).
                    with.property('paramName', 'pcs');
            });

            it(`should call trySetCanceled on the provided pcs`, () => {
                const mock: PromiseCompletionSource<string> = {
                    trySetCanceled: spy(() => { })
                } as any;
                new Canceled<string>().tryApply(mock);
                mock.trySetCanceled.should.have.been.called();
            });
        });

        context(`property:result`, () => {
            it(`should throw OperationCanceledError`, () => {
                (() => new Canceled<string>().result).should.throw(OperationCanceledError);
            });
        });
    });
});
