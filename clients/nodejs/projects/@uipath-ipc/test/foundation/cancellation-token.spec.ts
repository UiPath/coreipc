import { MockError } from '../jest-extensions';

import { CancellationToken } from '../../src/foundation/tasks/cancellation-token';
import { CancellationTokenRegistration } from '../../src/foundation/tasks/cancellation-token-registration';
import { CancellationTokenSource } from '../../src/foundation/tasks/cancellation-token-source';
import { AggregateError } from '../../src/foundation/errors/aggregate-error';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';

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

});
