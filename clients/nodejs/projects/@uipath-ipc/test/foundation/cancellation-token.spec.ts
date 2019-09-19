import { MockError } from '../jest-extensions';

import { CancellationToken } from '../../src/foundation/tasks/cancellation-token';
import { CancellationTokenRegistration } from '../../src/foundation/tasks/cancellation-token-registration';
import { CancellationTokenSource } from '../../src/foundation/tasks/cancellation-token-source';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';
import { ArgumentError } from '../../src/foundation/errors/argument-error';
import '../../src/foundation/tasks/promise-pal';

describe('Foundation-CancellationToken', () => {

    test(`none works`, () => {
        expect(() => CancellationToken.none).not.toThrow();
        expect(CancellationToken.none).toBe(CancellationToken.none);
        expect(() => CancellationToken.none.isCancellationRequested).not.toThrow();
        expect(CancellationToken.none.isCancellationRequested).toBe(false);
        expect(() => CancellationToken.none.register(() => { })).not.toThrow();
        expect(CancellationToken.none.register(() => { })).toBe(CancellationTokenRegistration.none);
        expect(() => CancellationToken.none.register(() => { }).dispose()).not.toThrow();
    });

    test(`register works`, () => {
        const mockCallback = jest.fn();

        const cts = new CancellationTokenSource();
        const token = cts.token;

        expect(() => token.register(mockCallback)).not.toThrow();
        expect(mockCallback).not.toHaveBeenCalled();

        cts.cancel();
        expect(mockCallback).toHaveBeenCalled();

        const mock2 = jest.fn();
        expect(() => token.register(mock2)).not.toThrow();
        expect(mock2).toHaveBeenCalled();
    });

    test(`register doesn't hide callback exceptions`, () => {
        const mockError = new MockError();
        const mockCallback = jest.fn(() => { throw mockError; });

        const cts = new CancellationTokenSource();
        const token = cts.token;

        expect(mockCallback).not.toHaveBeenCalled();
        expect(() => token.register(mockCallback)).not.toThrow();

        try { cts.cancel(); } catch (_) { }

        expect(mockCallback).toHaveBeenCalled();
        expect(() => token.register(mockCallback)).toThrowInstance(mockError);
    });

    test(`throwIfCancellationRequested works`, () => {
        const cts = new CancellationTokenSource();
        const token = cts.token;

        expect(() => token.throwIfCancellationRequested()).not.toThrow();
        cts.cancel();
        expect(() => token.throwIfCancellationRequested()).toThrowInstanceOf(OperationCanceledError);
    });

    test(`CancellationToken.merge throws for empty array`, () => {
        expect(() => CancellationToken.merge())
            .toThrowInstanceOf(ArgumentError, x => x.message === ArgumentError.computeMessage('No tokens were supplied.', 'tokens') && x.maybeParamName === 'tokens');
    });

    test(`CancellationToken.merge returns same token from singleton array`, () => {
        const token = new CancellationTokenSource().token;
        expect(CancellationToken.merge(token)).toBe(token);
    });

    test(`CancellationToken.merge works`, () => {
        const cts1 = new CancellationTokenSource();
        const cts2 = new CancellationTokenSource();
        const cts3 = new CancellationTokenSource();

        const mergedToken = CancellationToken.merge(cts1.token, cts2.token, cts2.token);

        expect(mergedToken.canBeCanceled).toBe(true);
        expect(mergedToken.isCancellationRequested).toBe(false);

        const mock1 = jest.fn();
        mergedToken.register(mock1);

        expect(mock1).not.toHaveBeenCalled();
        cts1.cancel();
        expect(mergedToken.isCancellationRequested).toBe(true);
        expect(mock1).toHaveBeenCalledTimes(1);

        cts2.cancel();
        expect(mock1).toHaveBeenCalledTimes(1);
        cts3.cancel();
        expect(mock1).toHaveBeenCalledTimes(1);
    });
});
