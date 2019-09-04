import '../jest-extensions';
import * as Outcome from '../../src/foundation/outcome';
import { InvalidOperationError } from '../../src/foundation/errors/invalid-operation-error';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';

describe('Foundation-Outcome', () => {

    test(`Succeeded.ctor doesn't throw`, () => {
        expect(() => new Outcome.Succeeded(123)).not.toThrow();
    });
    test(`Faulted.ctor doesn't throw`, () => {
        expect(() => new Outcome.Faulted(new InvalidOperationError())).not.toThrow();
    });
    test(`Canceled.getInstance doesn't throw`, () => {
        expect(() => new Outcome.Canceled()).not.toThrow();
    });

    test(`isSucceeded doesn't throw`, () => {
        const outcome1: Outcome.Any<number> = new Outcome.Succeeded(123);
        const outcome2: Outcome.Any<number> = new Outcome.Faulted(new InvalidOperationError());
        const outcome3: Outcome.Any<number> = new Outcome.Canceled();

        expect(() => outcome1.isSucceeded()).not.toThrow();
        expect(() => outcome2.isSucceeded()).not.toThrow();
        expect(() => outcome3.isSucceeded()).not.toThrow();
    });

    test(`isFaulted doesn't throw`, () => {
        const outcome1: Outcome.Any<number> = new Outcome.Succeeded(123);
        const outcome2: Outcome.Any<number> = new Outcome.Faulted(new InvalidOperationError());
        const outcome3: Outcome.Any<number> = new Outcome.Canceled();

        expect(() => outcome1.isFaulted()).not.toThrow();
        expect(() => outcome2.isFaulted()).not.toThrow();
        expect(() => outcome3.isFaulted()).not.toThrow();
    });

    test(`isCanceled doesn't throw`, () => {
        const outcome1: Outcome.Any<number> = new Outcome.Succeeded(123);
        const outcome2: Outcome.Any<number> = new Outcome.Faulted(new InvalidOperationError());
        const outcome3: Outcome.Any<number> = new Outcome.Canceled();

        expect(() => outcome1.isCanceled()).not.toThrow();
        expect(() => outcome2.isCanceled()).not.toThrow();
        expect(() => outcome3.isCanceled()).not.toThrow();
    });

    test(`Consistency`, () => {
        const outcome1: Outcome.Any<number> = new Outcome.Succeeded(123);
        const outcome2: Outcome.Any<number> = new Outcome.Faulted(new InvalidOperationError());
        const outcome3: Outcome.Any<number> = new Outcome.Canceled();

        expect(outcome1.isSucceeded()).toBe(true);
        expect(outcome1.isFaulted()).toBe(false);
        expect(outcome1.isCanceled()).toBe(false);

        expect(outcome2.isSucceeded()).toBe(false);
        expect(outcome2.isFaulted()).toBe(true);
        expect(outcome2.isCanceled()).toBe(false);

        expect(outcome3.isSucceeded()).toBe(false);
        expect(outcome3.isFaulted()).toBe(false);
        expect(outcome3.isCanceled()).toBe(true);

        expect(outcome1.result).toBe(123);
        expect(() => outcome2.result).toThrowInstanceOf(InvalidOperationError);
        expect(() => outcome3.result).toThrowInstanceOf(OperationCanceledError);
    });

    test(`apply and tryApply should work`, () => {
        const createMock = () => ({
            setError: jest.fn(),
            setResult: jest.fn(),
            setCanceled: jest.fn(),
            trySetError: jest.fn(),
            trySetResult: jest.fn(),
            trySetCanceled: jest.fn(),
        });

        const outcome1 = new Outcome.Succeeded(123);
        const outcome2 = new Outcome.Faulted(new InvalidOperationError());
        const outcome3 = new Outcome.Canceled();

        const mock1 = createMock();
        const mock2 = createMock();
        const mock3 = createMock();
        const mock4 = createMock();
        const mock5 = createMock();
        const mock6 = createMock();

        outcome1.apply(mock1 as any);
        outcome2.apply(mock2 as any);
        outcome3.apply(mock3 as any);

        outcome1.tryApply(mock4 as any);
        outcome2.tryApply(mock5 as any);
        outcome3.tryApply(mock6 as any);

        expect(mock1.setError).not.toHaveBeenCalled();
        expect(mock1.setResult).toHaveBeenCalledTimes(1);
        expect(mock1.setResult).toHaveBeenCalledWith(123);
        expect(mock1.setCanceled).not.toHaveBeenCalled();
        expect(mock1.trySetError).not.toHaveBeenCalled();
        expect(mock1.trySetResult).not.toHaveBeenCalled();
        expect(mock1.trySetCanceled).not.toHaveBeenCalled();

        expect(mock2.setError).toHaveBeenCalledTimes(1);
        expect(mock2.setError).toHaveBeenCalledWith(new InvalidOperationError());
        expect(mock2.setResult).not.toHaveBeenCalled();
        expect(mock2.setCanceled).not.toHaveBeenCalled();
        expect(mock2.trySetError).not.toHaveBeenCalled();
        expect(mock2.trySetResult).not.toHaveBeenCalled();
        expect(mock2.trySetCanceled).not.toHaveBeenCalled();

        expect(mock3.setError).not.toHaveBeenCalled();
        expect(mock3.setResult).not.toHaveBeenCalled();
        expect(mock3.setCanceled).toHaveBeenCalledTimes(1);
        expect(mock3.setCanceled).toHaveBeenCalledWith();
        expect(mock3.trySetError).not.toHaveBeenCalled();
        expect(mock3.trySetResult).not.toHaveBeenCalled();
        expect(mock3.trySetCanceled).not.toHaveBeenCalled();

        expect(mock4.setError).not.toHaveBeenCalled();
        expect(mock4.setResult).not.toHaveBeenCalled();
        expect(mock4.setCanceled).not.toHaveBeenCalled();
        expect(mock4.trySetError).not.toHaveBeenCalled();
        expect(mock4.trySetResult).toHaveBeenCalledTimes(1);
        expect(mock4.trySetResult).toHaveBeenCalledWith(123);
        expect(mock4.trySetCanceled).not.toHaveBeenCalled();

        expect(mock5.setError).not.toHaveBeenCalled();
        expect(mock5.setResult).not.toHaveBeenCalled();
        expect(mock5.setCanceled).not.toHaveBeenCalled();
        expect(mock5.trySetError).toHaveBeenCalledTimes(1);
        expect(mock5.trySetError).toHaveBeenCalledWith(new InvalidOperationError());
        expect(mock5.trySetResult).not.toHaveBeenCalled();
        expect(mock5.trySetCanceled).not.toHaveBeenCalled();

        expect(mock6.setError).not.toHaveBeenCalled();
        expect(mock6.setResult).not.toHaveBeenCalled();
        expect(mock6.setCanceled).not.toHaveBeenCalled();
        expect(mock6.trySetError).not.toHaveBeenCalled();
        expect(mock6.trySetResult).not.toHaveBeenCalled();
        expect(mock6.trySetCanceled).toHaveBeenCalledTimes(1);
        expect(mock6.trySetCanceled).toHaveBeenCalledWith();
    });
});
