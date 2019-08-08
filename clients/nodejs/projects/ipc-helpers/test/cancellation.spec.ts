import { CancellationToken, SimpleCancellationToken } from '../src/cancellation-token/cancellation-token';
import { CancellationTokenSource } from '../src/cancellation-token/cancellation-token-source';
import { PromiseHelper } from '../src/promises/promise-helper';

describe('CancellationToken and CancellationTokenSource', () => {

    describe('Commons', () => {

        test('cancel-and-isCancellationRequested-work', () => {
            const source = new CancellationTokenSource();
            const token = source.token;
            expect(token.isCancellationRequested).toBeFalsy();
            source.cancel();
            expect(token.isCancellationRequested).toBeTruthy();
        });

        test('cancelAfter-and-isCancellationRequested-works', async () => {
            const source = new CancellationTokenSource();
            const token = source.token;

            expect(token.isCancellationRequested).toBeFalsy();

            source.cancelAfter(100);
            await PromiseHelper.delay(20);

            expect(token.isCancellationRequested).toBeFalsy();

            await PromiseHelper.delay(100);
            expect(token.isCancellationRequested).toBeTruthy();
        });

        test('register-works', () => {
            const mockHandler = jest.fn();
            const source = new CancellationTokenSource();
            const token = source.token;
            token.register(mockHandler);

            expect(mockHandler).not.toHaveBeenCalled();
            source.cancel();
            expect(mockHandler).toHaveBeenCalledTimes(1);
            source.cancel();
            expect(mockHandler).toHaveBeenCalledTimes(1);

            const mockHandler2 = jest.fn();
            expect(mockHandler2).not.toHaveBeenCalled();
            token.register(mockHandler2);
            expect(mockHandler2).toHaveBeenCalledTimes(1);
        });

        test('cancel-throwOnFirstError-works', () => {
            const throwOnFirstError = true;
            const mockHandler1 = jest.fn(() => { throw new Error('error 1'); });
            const mockHandler2 = jest.fn(() => { throw new Error('error 2'); });
            const mockHandler3 = jest.fn(() => { throw new Error('error 3'); });

            const source = new CancellationTokenSource();
            const token = source.token;

            token.register(mockHandler1);
            token.register(mockHandler2);
            token.register(mockHandler3);

            expect(() => source.cancel(throwOnFirstError)).toThrow('error 1');

            expect(mockHandler1).toHaveBeenCalled();
            expect(mockHandler2).not.toHaveBeenCalled();
            expect(mockHandler3).not.toHaveBeenCalled();
        });

        test('cancel-throwOnFirstError-false-works', () => {
            const throwOnFirstError = false;
            const mockHandler1 = jest.fn(() => { throw new Error('error 1'); });
            const mockHandler2 = jest.fn(() => { throw new Error('error 2'); });
            const mockHandler3 = jest.fn(() => { throw new Error('error 3'); });

            const source = new CancellationTokenSource();
            const token = source.token;

            token.register(mockHandler1);
            token.register(mockHandler2);
            token.register(mockHandler3);

            expect(() => source.cancel(throwOnFirstError)).toThrow();

            expect(mockHandler1).toHaveBeenCalled();
            expect(mockHandler2).toHaveBeenCalled();
            expect(mockHandler3).toHaveBeenCalled();
        });

        test('cancel-unregister-works', () => {
            const mockHandler = jest.fn();
            const source = new CancellationTokenSource();
            const token = source.token;

            const registration = token.register(mockHandler);
            expect(registration).not.toBeNull();

            expect(() => registration.dispose()).not.toThrow();
            source.cancel();

            expect(mockHandler).not.toHaveBeenCalled();
            expect(() => registration.dispose()).not.toThrow();
        });

    });

    describe('CancellationTokenSource', () => {
        test('ctor-doesnt-throw', () => {
            expect(() => new CancellationTokenSource()).not.toThrow();
        });
        test('token-works', () => {
            const source = new CancellationTokenSource();
            expect(source.token).toBeInstanceOf(CancellationToken);
        });
        test('cancel-doesnt-throw', () => {
            const source = new CancellationTokenSource();
            expect(() => source.cancel()).not.toThrow();
        });
        test('cancelAfter-doesnt-throw', () => {
            const source = new CancellationTokenSource();
            expect(() => source.cancelAfter(100)).not.toThrow();
        });
        test('cancelAfter-resets-gracefully', async () => {
            const mockHandler = jest.fn();
            const source = new CancellationTokenSource();
            const token = source.token;

            token.register(mockHandler);
            source.cancelAfter(50);
            await PromiseHelper.delay(10);

            expect(mockHandler).not.toHaveBeenCalled();

            for (let i = 0; i < 5; i++) {
                source.cancelAfter(50);
                await PromiseHelper.delay(10);
                expect(mockHandler).not.toHaveBeenCalled();
            }

            await PromiseHelper.delay(60);
            expect(mockHandler).toHaveBeenCalled();

            expect(() => source.cancelAfter(1)).not.toThrow();
        });
    });

    describe('CancellationToken', () => {
        test('ctor-throws-without-CancellationTokenSource', () => {
            expect(() => {
                const foo = new SimpleCancellationToken(null as any);
            }).toThrow();
            expect(() => {
                const foo = new SimpleCancellationToken(undefined as any);
            }).toThrow();
            expect(() => {
                const foo = new SimpleCancellationToken('something wrong' as any);
            }).toThrow();
        });

        test('ctor-doesnt-throw-with-c', () => {
            const ensuredSource = {
                __proto__: CancellationTokenSource.prototype
            } as any as CancellationTokenSource;

            expect(() => {
                const foo = new SimpleCancellationToken(ensuredSource);
            }).not.toThrow();
        });

        test('register-doesnt-throw', () => {
            const source = new CancellationTokenSource();
            const token = source.token;
            const mockHandler = jest.fn();
            expect(() => {
                token.register(mockHandler);
            }).not.toThrow();
        });

        test('isCancellationRequested-doesnt-throw', () => {
            const source = new CancellationTokenSource();
            const token = source.token;
            expect(() => {
                const foo = token.isCancellationRequested;
            }).not.toThrow();
        });

        test('defaultIfFalsy-returns-as-expected', () => {
            expect(CancellationToken.defaultIfFalsy(null)).toBe(CancellationToken.default);
            expect(CancellationToken.defaultIfFalsy(undefined)).toBe(CancellationToken.default);
            expect(CancellationToken.defaultIfFalsy(CancellationToken.default)).toBe(CancellationToken.default);

            const cts = new CancellationTokenSource();
            expect(CancellationToken.defaultIfFalsy(cts.token)).toBe(cts.token);
        });

        test('combine-works', () => {
            expect(CancellationToken.combine()).toBe(CancellationToken.default);
            expect(CancellationToken.combine(CancellationToken.default)).toBe(CancellationToken.default);

            const cts1 = new CancellationTokenSource();
            expect(CancellationToken.combine(cts1.token)).toBe(cts1.token);

            const cts2 = new CancellationTokenSource();
            let combined: CancellationToken | null = null;
            expect(() => {
                combined = CancellationToken.combine(cts1.token, cts2.token);
            }).not.toThrow();

            expect(combined).toBeInstanceOf(CancellationToken);

            // making the TypeScript compiler happy:
            const combined2 = combined as any as CancellationToken;

            expect(combined2.isCancellationRequested).toBe(false);
            const handler = jest.fn();
            combined2.register(handler);

            expect(handler).not.toHaveBeenCalled();

            cts1.cancel();

            expect(combined2.isCancellationRequested).toBe(true);
            expect(handler).toHaveBeenCalled();
        });
    });

    describe('DefaultCancellationTokenSource', () => {
        test('register-doesnt-throw', () => {
            const mockHandler = jest.fn();
            const source = CancellationTokenSource.default;
            expect(() => source.register(mockHandler)).not.toThrow();
        });
        test('cancel-doesnt-throw', () => {
            const mockHandler = jest.fn();
            const source = CancellationTokenSource.default;
            expect(() => source.cancel()).not.toThrow();
            expect(() => source.cancel(false)).not.toThrow();
            expect(() => source.cancel(true)).not.toThrow();
        });
        test('cancelAfter-doesnt-throw', () => {
            const mockHandler = jest.fn();
            const source = CancellationTokenSource.default;
            expect(() => source.cancelAfter(10)).not.toThrow();
        });
    });

});
