import '../jest-extensions';
import { CancellationTokenSource } from '../../src/foundation/tasks/cancellation-token-source';
import { PromisePal } from '../../src/foundation/tasks/promise-pal';
import { AggregateError } from '../../src/foundation/errors/aggregate-error';
import { TimeSpan } from '../../src/foundation/tasks/timespan';
import { ObjectDisposedError } from '../../src/foundation/errors/object-disposed-error';
import { MockError } from '../jest-extensions';

describe('Foundation-CancellationTokenSource', () => {
    test(`ctor doesn't throw`, () => {
        expect(() => new CancellationTokenSource()).not.toThrow();
    });
    test(`cancel doesn't throw with no registrations`, () => {
        const cts = new CancellationTokenSource();
        expect(() => cts.cancel()).not.toThrow();
    });
    test(`dispose doesn't throw, is idempotent and works`, () => {
        const cts = new CancellationTokenSource();
        expect(() => cts.dispose()).not.toThrow();
        expect(() => cts.dispose()).not.toThrow();
        expect(() => cts.cancel()).toThrowInstanceOf(ObjectDisposedError, error => error.objectName === 'CancellationTokenSource');
        expect(() => cts.cancelAfter(TimeSpan.fromMilliseconds(1))).toThrowInstanceOf(ObjectDisposedError, error => error.objectName === 'CancellationTokenSource');

        const cts2 = new CancellationTokenSource();
        cts2.cancel();
        expect(() => cts2.dispose()).not.toThrow();
        expect(() => cts2.dispose()).not.toThrow();
        expect(() => cts2.cancel()).toThrowInstanceOf(ObjectDisposedError, error => error.objectName === 'CancellationTokenSource');
        expect(() => cts2.cancelAfter(TimeSpan.fromMilliseconds(1))).toThrowInstanceOf(ObjectDisposedError, error => error.objectName === 'CancellationTokenSource');

        const cts3 = new CancellationTokenSource();
        cts3.cancelAfter(TimeSpan.fromMilliseconds(1));
        expect(() => cts3.dispose()).not.toThrow();
        expect(() => cts3.dispose()).not.toThrow();
        expect(() => cts3.cancel()).toThrowInstanceOf(ObjectDisposedError, error => error.objectName === 'CancellationTokenSource');
        expect(() => cts3.cancelAfter(TimeSpan.fromMilliseconds(1))).toThrowInstanceOf(ObjectDisposedError, error => error.objectName === 'CancellationTokenSource');
    });
    test(`cancel throws aggregate for throwOnFirstError === undefined`, () => {
        const errors = [new Error(), new Error(), new Error()];
        const mock0 = jest.fn();
        const mock1 = jest.fn(() => { throw errors[0]; });
        const mock2 = jest.fn(() => { throw errors[1]; });
        const mock3 = jest.fn(() => { throw errors[2]; });

        const cts = new CancellationTokenSource();
        cts.token.register(mock0);
        cts.token.register(mock1);
        cts.token.register(mock2);
        cts.token.register(mock3);

        expect(() => cts.cancel()).toThrowInstanceOf(AggregateError, error =>
            error.errors.length === 3 &&
            error.errors[0] === errors[0] &&
            error.errors[1] === errors[1] &&
            error.errors[2] === errors[2]
        );
        expect(() => cts.cancel()).not.toThrow();
        expect(() => cts.cancel(false)).not.toThrow();
        expect(() => cts.cancel(true)).not.toThrow();
    });
    test(`cancel throws aggregate for throwOnFirstError === false`, () => {
        const errors = [new Error(), new Error(), new Error()];
        const working = jest.fn();
        const throwing1 = jest.fn(() => { throw errors[0]; });
        const throwing2 = jest.fn(() => { throw errors[1]; });
        const throwing3 = jest.fn(() => { throw errors[2]; });

        const cts = new CancellationTokenSource();
        cts.token.register(working);
        cts.token.register(throwing1);
        cts.token.register(throwing2);
        cts.token.register(throwing3);

        expect(() => cts.cancel(false)).toThrowInstanceOf(AggregateError, error =>
            error.errors.length === 3 &&
            error.errors[0] === errors[0] &&
            error.errors[1] === errors[1] &&
            error.errors[2] === errors[2]
        );
        expect(() => cts.cancel()).not.toThrow();
        expect(() => cts.cancel(false)).not.toThrow();
        expect(() => cts.cancel(true)).not.toThrow();
    });
    test(`cancel throws the 1st error for throwOnFirstError === true`, () => {
        const errors = [new Error(), new Error(), new Error()];
        const mock0 = jest.fn();
        const mock1 = jest.fn(() => { throw errors[0]; });
        const mock2 = jest.fn(() => { throw errors[1]; });
        const mock3 = jest.fn(() => { throw errors[2]; });

        const cts = new CancellationTokenSource();
        cts.token.register(mock0);
        cts.token.register(mock1);
        cts.token.register(mock2);
        cts.token.register(mock3);

        expect(() => cts.cancel(true)).toThrowInstanceOf(Error, error => error === errors[0]);
        expect(() => cts.cancel()).not.toThrow();
        expect(() => cts.cancel(false)).not.toThrow();
        expect(() => cts.cancel(true)).not.toThrow();
    });
    test(`cancelAfter works`, async () => {
        const mock = jest.fn();
        const cts = new CancellationTokenSource();
        expect(() => cts.cancelAfter(TimeSpan.fromMilliseconds(10))).not.toThrow();
        cts.token.register(mock);

        expect(mock).not.toHaveBeenCalled();
        await PromisePal.delay(TimeSpan.fromMilliseconds(15));

        expect(mock).toHaveBeenCalledTimes(1);

        expect(() => cts.cancelAfter(TimeSpan.fromMilliseconds(10))).not.toThrow();
    });
    test(`calling cancelAfter a second time has no effect`, async () => {
        const mock = jest.fn();
        const cts = new CancellationTokenSource();
        cts.token.register(mock);
        cts.cancelAfter(TimeSpan.fromMilliseconds(10));
        cts.cancelAfter(TimeSpan.fromMilliseconds(50));
        expect(mock).not.toHaveBeenCalled();
        await PromisePal.delay(TimeSpan.fromMilliseconds(30));
        expect(mock).toHaveBeenCalledTimes(1);
        await PromisePal.delay(TimeSpan.fromMilliseconds(200));
        expect(mock).toHaveBeenCalledTimes(1);
    });

    test(`cancel doesn't do anything the 2nd time it's called`, () => {
        const mockError = new MockError();
        const mockCallback = jest.fn(() => { throw mockError; });

        const testCases: Array<{
            throwOnFirstError: boolean | undefined;
            validate: (matchers: jest.Matchers<void>) => void
        }> = [
                { throwOnFirstError: undefined, validate: x => x.toThrowInstanceOf(AggregateError, error => error.errors.length === 1 && error.errors[0] === mockError) },
                { throwOnFirstError: false, validate: x => x.toThrowInstanceOf(AggregateError, error => error.errors.length === 1 && error.errors[0] === mockError) },
                { throwOnFirstError: true, validate: x => x.toThrowInstance(mockError) },
            ];

        for (const testCase of testCases) {
            const cts = new CancellationTokenSource();
            try {
                const token = cts.token;
                token.register(mockCallback);

                testCase.validate(expect(() => cts.cancel(testCase.throwOnFirstError)));
                expect(() => cts.cancel()).not.toThrow();
            } finally {
                cts.dispose();
            }
        }
    });
});
