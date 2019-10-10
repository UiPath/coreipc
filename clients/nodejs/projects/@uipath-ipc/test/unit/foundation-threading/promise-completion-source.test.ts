// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, should, spy, use } from 'chai';
import spies from 'chai-spies';

import { PromiseCompletionSource } from '@foundation/threading';
import { InvalidOperationError } from '@foundation/errors';
import { AnyOutcome, Succeeded, Faulted, Canceled } from '@foundation/utils/outcome';

use(spies);

describe(`foundation:threading -> class:PromiseCompletionSource`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            expect(() => new PromiseCompletionSource<string>()).not.to.throw();
        });
    });

    context(`property:promise`, () => {
        it(`shouldn't throw`, () => {
            const pcs = new PromiseCompletionSource<string>();
            expect(() => pcs.promise).not.to.throw();
        });
        it(`should return a truthy reference`, () => {
            const pcs = new PromiseCompletionSource<string>();
            const promise = pcs.promise;
            expect(promise).not.to.be.null;
            expect(promise).not.to.be.undefined;
        });
        it(`should return the same thing over and over`, () => {
            const pcs = new PromiseCompletionSource<string>();
            expect(pcs.promise).to.equal(pcs.promise);
        });
        it(`should return a Promise`, () => {
            const pcs = new PromiseCompletionSource<string>();
            expect(pcs.promise).to.be.instanceOf(Promise);
        });
    });

    const _setCases: Array<{
        testName: string,
        call: (pcs: PromiseCompletionSource<string>) => void,
        isPrimary?: boolean
    }> = [
            { isPrimary: true, testName: 'setResult', call: x => x.setResult('foo') },
            { isPrimary: true, testName: 'setError', call: x => x.setError(new Error()) },
            { isPrimary: true, testName: 'setCanceled', call: x => x.setCanceled() },
            { testName: 'set with a Succeeded outcome', call: x => x.set(new Succeeded('foo')) },
            { testName: 'set with a Faulted outcome', call: x => x.set(new Faulted<string>(new Error())) },
            { testName: 'set with a Canceled outcome', call: x => x.set(new Canceled<string>()) }
        ];

    for (const _context of _setCases) {
        context(`method:${_context.testName}`, () => {
            it(`shouldn't throw when it's the 1st completing method which is called`, () => {
                const pcs = new PromiseCompletionSource<string>();
                expect(() => _context.call(pcs)).not.to.throw();
            });
            it(`should throw when it's not the 1st completing method which is called`, () => {
                for (const _case of _setCases.filter(x => !!x.isPrimary)) {
                    const pcs = new PromiseCompletionSource<string>();
                    _case.call(pcs);

                    expect(() => _context.call(pcs)).to.throw(InvalidOperationError);
                }
            });
        });
    }

    const _trySetCases: Array<{
        testName: string,
        call: (pcs: PromiseCompletionSource<string>) => void,
        isPrimary?: boolean
    }> = [
            { isPrimary: true, testName: 'trySetResult', call: x => x.trySetResult('foo') },
            { isPrimary: true, testName: 'trySetError', call: x => x.trySetError(new Error()) },
            { isPrimary: true, testName: 'trySetCanceled', call: x => x.trySetCanceled() },
            { testName: 'trySet with a Succeeded outcome', call: x => x.trySet(new Succeeded('foo')) },
            { testName: 'trySet with a Faulted outcome', call: x => x.trySet(new Faulted<string>(new Error())) },
            { testName: 'trySet with a Canceled outcome', call: x => x.trySet(new Canceled<string>()) }
        ];

    for (const _context of _trySetCases) {
        context(`method:${_context.testName}`, () => {
            it(`shouldn't throw when it's the 1st completing method which is called`, () => {
                const pcs = new PromiseCompletionSource<string>();
                expect(() => _context.call(pcs)).not.to.throw();
            });
            it(`shouldn't throw when it's not the 1st completing method which is called`, () => {
                for (const _case of _setCases.filter(x => !!x.isPrimary)) {
                    const pcs = new PromiseCompletionSource<string>();
                    _case.call(pcs);

                    expect(() => _context.call(pcs)).not.to.throw(InvalidOperationError);
                }
            });
            it(`should return true when it's the 1st completing method which is called`, () => {
                const pcs = new PromiseCompletionSource<string>();
                expect(_context.call(pcs)).to.be.true;
            });
            it(`should return false when it's not the 1st completing method which is called`, () => {
                for (const _case of _setCases.filter(x => !!x.isPrimary)) {
                    const pcs = new PromiseCompletionSource<string>();
                    _case.call(pcs);

                    expect(_context.call(pcs)).to.be.false;
                }
            });
        });
    }
});
