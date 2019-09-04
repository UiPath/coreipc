import '../jest-extensions';
import { measure } from '../jest-extensions';

import { PromisePal } from '../../src/foundation/tasks/promise-pal';
import { TimeSpan } from '../../src/foundation/tasks/timespan';
import { ArgumentError } from '../../src/foundation/errors/argument-error';
import { CancellationTokenSource } from '../../src/foundation/tasks/cancellation-token-source';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';
import { AbstractMemberError } from '../../src/foundation/errors/abstract-member-error';

describe('Foundation-PromisePal', () => {
    test(`yield doesn't throw`, async () => {
        let promise: Promise<void> | null = null;
        expect(() => promise = PromisePal.yield()).not.toThrow();
        await expect(promise).resolves.toBeUndefined();
    }, 100);

    test(`yield works`, async () => {
        await expect(PromisePal.yield()).resolves.toBeUndefined();
    }, 100);

    test(`delay doesn't throw when it shouldn't`, async () => {
        const spans = [TimeSpan.zero, TimeSpan.fromMilliseconds(1), TimeSpan.fromMilliseconds(10)];
        for (const span of spans) {
            let promise: Promise<void> | null = null;
            expect(() => promise = PromisePal.delay(span)).not.toThrow();
            await expect(promise).resolves.toBeUndefined();
        }
    }, 100);

    test(`delay throws when it should`, () => {
        expect(() => PromisePal.delay(TimeSpan.fromMinutes(-2))).toThrowInstanceOf(ArgumentError, error => error.maybeParamName === 'timespan');
    });

    test(`delay works`, async () => {
        const expected = TimeSpan.fromMilliseconds(100);
        const actual = await measure(() => PromisePal.delay(expected));
        expect(Math.abs(actual.totalSeconds - expected.totalSeconds)).toBeLessThanOrEqual(100);
    });

    test(`delay works with ct`, async () => {
        const cts = new CancellationTokenSource();
        const promise = PromisePal.delay(TimeSpan.fromMilliseconds(100), cts.token);
        cts.cancel();
        await expect(promise).rejects.toBeInstanceOf(OperationCanceledError);
    });

    test(`delay works with ct 2`, async () => {
        const cts = new CancellationTokenSource();
        const promise = PromisePal.delay(TimeSpan.fromMilliseconds(50), cts.token);
        const timeoutId = setTimeout(() => cts.cancel(), 100);
        await expect(promise).resolves.toBeUndefined();
        clearTimeout(timeoutId);
    });

    test(`completedPromise works`, async () => {
        expect(() => PromisePal.completedPromise).not.toThrow();
        expect(PromisePal.completedPromise).toBe(PromisePal.completedPromise);
        await expect(PromisePal.completedPromise).resolves.toBeUndefined();

        const mock = jest.fn();
        PromisePal.completedPromise.then(mock, mock);
        expect(mock).not.toHaveBeenCalled();
        await PromisePal.yield();
        expect(mock).toHaveBeenCalledTimes(1);
    });

    test(`fromResult works`, async () => {
        let promise: Promise<number> | null = null;
        expect(() => promise = PromisePal.fromResult(123)).not.toThrow();

        const mock = jest.fn();
        promise.then(mock, mock);
        expect(mock).not.toHaveBeenCalled();
        await PromisePal.yield();
        expect(mock).toHaveBeenCalledWith(123);
        expect(mock).toHaveBeenCalledTimes(1);
        await expect(promise).resolves.toBe(123);
    });

    test(`fromError works`, async () => {
        let promise: Promise<number> | null = null;
        expect(() => promise = PromisePal.fromError(new AbstractMemberError('some error'))).not.toThrow();

        const mock = jest.fn();
        promise.then(mock, mock);
        expect(mock).not.toHaveBeenCalled();
        await PromisePal.yield();
        expect(mock).toHaveBeenCalledWith(new AbstractMemberError('some error'));
        expect(mock).toHaveBeenCalledTimes(1);
        await expect(promise).rejects.toBeInstanceOf(AbstractMemberError);
    });

    test(`fromCanceled works`, async () => {
        let promise: Promise<number> | null = null;
        expect(() => promise = PromisePal.fromCanceled()).not.toThrow();

        const mock = jest.fn();
        promise.then(mock, mock);
        expect(mock).not.toHaveBeenCalled();
        await PromisePal.yield();
        expect(mock).toHaveBeenCalledWith(new OperationCanceledError());
        expect(mock).toHaveBeenCalledTimes(1);
        await expect(promise).rejects.toBeInstanceOf(OperationCanceledError);
    });
});
