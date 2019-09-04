// tslint:disable: max-line-length
import '../jest-extensions';
import * as Outcome from '../../src/foundation/outcome';
import { PromiseCompletionSource } from '../../src/foundation/tasks/promise-completion-source';
import { InvalidOperationError } from '../../src/foundation/errors/invalid-operation-error';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';

describe('Foundation-PromiseCompletionSource', () => {

    test(`ctor doesn't throw`, () => {
        expect(() => new PromiseCompletionSource<void>()).not.toThrow();
    });
    test(`set outcome works`, async () => {
        const pcs1 = new PromiseCompletionSource<number>();
        expect(() => pcs1.set(new Outcome.Succeeded(123))).not.toThrow();
        await expect(pcs1.promise).resolves.toBe(123);

        const pcs2 = new PromiseCompletionSource<number>();
        expect(() => pcs2.set(new Outcome.Faulted(new InvalidOperationError()))).not.toThrow();
        await expect(pcs2.promise).rejects.toBeInstanceOf(InvalidOperationError);

        const pcs3 = new PromiseCompletionSource<number>();
        expect(() => pcs3.set(new Outcome.Canceled())).not.toThrow();
        await expect(pcs3.promise).rejects.toBeInstanceOf(OperationCanceledError);
    });
    test(`set throws when called a second time`, async () => {
        const pcs1 = new PromiseCompletionSource<number>();
        expect(() => pcs1.set(new Outcome.Succeeded(123))).not.toThrow();
        expect(() => pcs1.set(new Outcome.Succeeded(123))).toThrowInstanceOf(InvalidOperationError, error => error.message === PromiseCompletionSource._errorMessage);
        await expect(pcs1.promise).resolves.toBe(123);

        const pcs2 = new PromiseCompletionSource<number>();
        expect(() => pcs2.set(new Outcome.Faulted(new InvalidOperationError()))).not.toThrow();
        expect(() => pcs2.set(new Outcome.Faulted(new InvalidOperationError()))).toThrowInstanceOf(InvalidOperationError, error => error.message === PromiseCompletionSource._errorMessage);
        await expect(pcs2.promise).rejects.toBeInstanceOf(InvalidOperationError);

        const pcs3 = new PromiseCompletionSource<number>();
        expect(() => pcs3.set(new Outcome.Canceled())).not.toThrow();
        expect(() => pcs3.set(new Outcome.Canceled())).toThrowInstanceOf(InvalidOperationError, error => error.message === PromiseCompletionSource._errorMessage);
        await expect(pcs3.promise).rejects.toBeInstanceOf(OperationCanceledError);
    });

    test(`trySet outcome works and returns true the 1st time and false the 2nd time`, async () => {
        const pcs1 = new PromiseCompletionSource<number>();
        let result1: boolean | null = null;
        let result1b: boolean | null = null;
        expect(() => result1 = pcs1.trySet(new Outcome.Succeeded(123))).not.toThrow();
        expect(() => result1b = pcs1.trySet(new Outcome.Succeeded(123))).not.toThrow();
        expect(result1).toBe(true);
        expect(result1b).toBe(false);
        await expect(pcs1.promise).resolves.toBe(123);

        const pcs2 = new PromiseCompletionSource<number>();
        let result2: boolean | null = null;
        let result2b: boolean | null = null;
        expect(() => result2 = pcs2.trySet(new Outcome.Faulted(new InvalidOperationError()))).not.toThrow();
        expect(() => result2b = pcs2.trySet(new Outcome.Faulted(new InvalidOperationError()))).not.toThrow();
        expect(result2).toBe(true);
        expect(result2b).toBe(false);
        await expect(pcs2.promise).rejects.toBeInstanceOf(InvalidOperationError);

        const pcs3 = new PromiseCompletionSource<number>();
        let result3: boolean | null = null;
        let result3b: boolean | null = null;
        expect(() => result3 = pcs3.trySet(new Outcome.Canceled())).not.toThrow();
        expect(() => result3b = pcs3.trySet(new Outcome.Canceled())).not.toThrow();
        expect(result3).toBe(true);
        expect(result3b).toBe(false);
        await expect(pcs3.promise).rejects.toBeInstanceOf(OperationCanceledError);
    });

});
