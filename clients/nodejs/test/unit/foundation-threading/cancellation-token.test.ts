// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import spies from 'chai-spies';

import { CancellationTokenSource, CancellationToken, ProperCancellationToken } from '../../../src/foundation/threading';
import { ArgumentError, OperationCanceledError } from '../../../src/foundation/errors';

use(spies);

describe(`foundation:threading -> class:CancellationToken`, () => {
    context(`property:none`, () => {
        it(`shouldn't throw`, () => {
            expect(() => CancellationToken.none).not.to.throw();
        });

        it(`should return a truthy reference`, () => {
            expect(CancellationToken.none).not.to.be.null;
            expect(CancellationToken.none).not.to.be.undefined;
        });

        it(`should return a CancellationToken`, () => {
            expect(CancellationToken.none).instanceOf(CancellationToken);
        });

        it(`should return the same reference every time`, () => {
            expect(CancellationToken.none).to.equal(CancellationToken.none);
        });

        it(`should return a CancellationToken which isn't canceled`, () => {
            expect(CancellationToken.none.isCancellationRequested).to.be.false;
        });

        it(`should return a CancellationToken which can't be canceled`, () => {
            expect(CancellationToken.none.canBeCanceled).to.be.false;
        });

        it(`should return a CancellationToken which doesn't throw when invoking throwIfCancellationRequested`, () => {
            expect(() => CancellationToken.none.throwIfCancellationRequested()).not.to.throw();
        });
    });

    context(`method:merge`, () => {
        it(`should throw when called with zero tokens`, () => {
            expect(() => CancellationToken.merge()).to.throw(ArgumentError).with.property('paramName', 'tokens');
        });

        it(`shouldn't throw when called with one token`, () => {
            expect(() => CancellationToken.merge(new CancellationTokenSource().token)).not.to.throw();
        });

        const tokenCount = 4;

        it(`shouldn't throw when called with multiple tokens`, () => {
            expect(() => CancellationToken.merge(
                ...Array.from(Array(tokenCount).keys()).map(_ => new CancellationTokenSource().token)
            )).not.to.throw();
        });

        it(`should return a ct that becomes canceled when either provided ct becomes canceled`, () => {
            for (const i of Array.from(Array(tokenCount).keys()).keys()) {
                const sources = Array.from(Array(tokenCount).keys()).map(_ => new CancellationTokenSource());
                const tokens = sources.map(source => source.token);

                const merged = CancellationToken.merge(...tokens);
                expect(merged.canBeCanceled).to.be.true;
                expect(merged.isCancellationRequested).to.be.false;

                sources[i].cancel();
                expect(merged.isCancellationRequested).to.be.true;
            }
        });
    });

    context(`method:throwIfCancellationRequested`, () => {
        it(`shouldn't throw for a ct which hasn't been canceled`, () => {
            const token = new CancellationTokenSource().token;
            expect(() => token.throwIfCancellationRequested()).not.to.throw();
        });
        it(`should throw for a ct which has been canceled`, () => {
            const source = new CancellationTokenSource();
            const token = source.token;
            source.cancel();
            expect(() => token.throwIfCancellationRequested()).to.throw(OperationCanceledError);
        });
    });
});

describe(`foundation:threading -> class:RegistrarCancellationToken`, () => {
    context(`register`, () => {
        it(`shouldn't throw provided a truthy callback`, () => {
            const token = new ProperCancellationToken();
            expect(() => token.register(() => { })).not.to.throw();
        });
        it(`should throw provided a falsy callback`, () => {
            const token = new ProperCancellationToken();
            expect(() => token.register(null as any)).to.throw();
            expect(() => token.register(undefined as any)).to.throw();
        });
        it(`should register the provided callback so that it gets called when the token transitions to the canceled state`, () => {
            const token = new ProperCancellationToken();
            const spyHandler1 = spy(() => { });
            const spyHandler2 = spy(() => { });

            token.register(spyHandler1);
            token.register(spyHandler2);

            token.cancel(false);

            expect(spyHandler1).to.have.been.called();
            expect(spyHandler2).to.have.been.called();

            token.cancel(false);

            expect(spyHandler1).not.to.have.been.called.twice;
            expect(spyHandler2).not.to.have.been.called.twice;
        });
        it(`should synchronously call the provided callback if the token is already in the canceled state`, () => {
            const token = new ProperCancellationToken();
            token.cancel(false);
            const spyHandler = spy(() => { });
            token.register(spyHandler);
            expect(spyHandler).to.have.been.called();
        });
    });
    context(`unregister`, () => {
        it(`shouldn't throw provided callback that had been registered earlier`, () => {
            const token = new ProperCancellationToken();
            const callback = () => { };
            token.register(callback);
            expect(() => token.unregister(callback)).not.to.throw();
        });
        it(`shouldn't throw provided callback that hadn't been registered earlier`, () => {
            const token = new ProperCancellationToken();
            const callback = () => { };
            expect(() => token.unregister(callback)).not.to.throw();
        });
        it(`should throw provided a falsy callback`, () => {
            const token = new ProperCancellationToken();
            expect(() => token.unregister(null as any)).to.throw();
            expect(() => token.unregister(undefined as any)).to.throw();
        });
        it(`should unregister the provided callback so that it will not be called when the token transitions to the canceled state`, () => {
            const token = new ProperCancellationToken();
            const spyHandler1 = spy(() => { });
            const spyHandler2 = spy(() => { });

            token.register(spyHandler1);
            token.register(spyHandler2);

            token.unregister(spyHandler1);
            token.unregister(spyHandler2);

            token.cancel(false);

            expect(spyHandler1).not.to.have.been.called();
            expect(spyHandler2).not.to.have.been.called();
        });
    });
});

describe(`foundation:threading -> class:ProperCancellationToken`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            expect(() => new ProperCancellationToken()).not.to.throw();
        });
    });
    context(`property:canBeCanceled`, () => {
        it(`shouldn't throw`, () => {
            const token = new ProperCancellationToken();
            expect(() => token.canBeCanceled).not.to.throw();
            expect(() => token.canBeCanceled).not.to.throw();
        });
        it(`should return true`, () => {
            const token = new ProperCancellationToken();
            expect(token.canBeCanceled).to.be.true;
            expect(token.canBeCanceled).to.be.true;
        });
    });
    context(`property:isCancellationRequested`, () => {
        it(`shouldn't throw`, () => {
            const token = new ProperCancellationToken();
            expect(() => token.isCancellationRequested).not.to.throw();
            expect(() => token.isCancellationRequested).not.to.throw();
            token.cancel(false);
            expect(() => token.isCancellationRequested).not.to.throw();
            expect(() => token.isCancellationRequested).not.to.throw();
        });
        it(`should return false when cancellation hadn't been requested`, () => {
            const token = new ProperCancellationToken();
            expect(token.isCancellationRequested).to.be.false;
            expect(token.isCancellationRequested).to.be.false;
        });
        it(`should return true when cancellation had been requested`, () => {
            const token = new ProperCancellationToken();
            token.cancel(false);
            expect(token.isCancellationRequested).to.be.true;
            expect(token.isCancellationRequested).to.be.true;
        });
    });
    context(`method:cancel`, () => {
        const cases = [false, true];

        it(`shouldn't throw (even if called multiple times)`, () => {
            for (const _case of cases) {
                const token = new ProperCancellationToken();
                expect(() => token.cancel(_case)).not.to.throw();
                expect(() => token.cancel(_case)).not.to.throw();
            }
        });
        it(`should cause the isCancellationRequested property to become true`, () => {
            for (const _case of cases) {
                const token = new ProperCancellationToken();
                token.cancel(_case);
                expect(token.isCancellationRequested).to.be.true;
            }
        });
        it(`should cause registered callbacks to be invoked`, () => {
            for (const _case of cases) {
                const token = new ProperCancellationToken();
                const spyHandler1 = spy(() => { });
                const spyHandler2 = spy(() => { });
                token.register(spyHandler1);
                token.register(spyHandler2);
                token.cancel(_case);
                expect(spyHandler1).to.have.been.called();
                expect(spyHandler2).to.have.been.called();
            }
        });
    });
});

describe(`foundation:threading -> class:NoneCancellationToken`, () => {
    context(`property:canBeCanceled`, () => {
        it(`shouldn't throw`, () => {
            expect(() => CancellationToken.none.canBeCanceled).not.to.throw();
            expect(() => CancellationToken.none.canBeCanceled).not.to.throw();
        });
        it(`should return false`, () => {
            expect(CancellationToken.none.canBeCanceled).to.be.false;
        });
    });
    context(`property:isCancellationRequested`, () => {
        it(`shouldn't throw`, () => {
            expect(() => CancellationToken.none.isCancellationRequested).not.to.throw();
            expect(() => CancellationToken.none.isCancellationRequested).not.to.throw();
        });
        it(`should return false (always)`, () => {
            expect(CancellationToken.none.isCancellationRequested).to.be.false;
        });
    });
    context(`method:register`, () => {
        it(`shouldn't throw (even provided a falsy callback)`, () => {
            const cases = [() => { }, null, undefined];
            for (const _case of cases) {
                expect(() => CancellationToken.none.register(_case as any)).not.to.throw();
            }
        });
    });
    context(`method:throwIfCancellationRequested`, () => {
        it(`shouldn't throw`, () => {
            expect(() => CancellationToken.none.throwIfCancellationRequested()).not.to.throw();
        });
    });
});
