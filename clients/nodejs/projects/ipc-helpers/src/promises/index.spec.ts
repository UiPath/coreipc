import { PromiseCompletionSource } from './promise-completion-source';
import { PromiseHelper } from './promise-helper';
import { CancellationToken } from '../cancellation-token/cancellation-token';
import { CancellationTokenSource } from '../cancellation-token/cancellation-token-source';

describe('PromiseCompletionSource<T>', () => {

    test('ctor-doesnt-throw', () => {
        expect(() => new PromiseCompletionSource<void>()).not.toThrow();
    });

    describe('general-consistency', () => {

        let pcs: PromiseCompletionSource<void>;

        beforeEach(() => pcs = new PromiseCompletionSource<void>());
        afterEach(() => {
            pcs.promise.catch(jest.fn());

            expect(() => pcs.setResult(undefined)).toThrow();
            expect(() => pcs.setException(new Error())).toThrow();
            expect(() => pcs.setCanceled()).toThrow();

            const results = new Array<boolean>();

            expect(() => results.push(pcs.trySetResult(undefined))).not.toThrow();
            expect(() => results.push(pcs.trySetException(new Error()))).not.toThrow();
            expect(() => results.push(pcs.trySetCanceled())).not.toThrow();

            expect(results).not.toContain(true);
        });

        test('setResult', () => {
            expect(() => pcs.setResult(undefined)).not.toThrow();
        });
        test('setException', () => {
            expect(() => pcs.setException(new Error())).not.toThrow();
        });
        test('setCancelled', () => {
            expect(() => pcs.setCanceled()).not.toThrow();
        });

        test('trySetResult', () => {
            let result: boolean | null = null;
            expect(() => result = pcs.trySetResult(undefined)).not.toThrow();
            expect(result).toBeTruthy();
        });
        test('trySetException', () => {
            let result: boolean | null = null;
            expect(() => result = pcs.trySetException(new Error())).not.toThrow();
            expect(result).toBeTruthy();
        });
        test('trySetCanceled', () => {
            let result: boolean | null = null;
            expect(() => result = pcs.trySetCanceled()).not.toThrow();
            expect(result).toBeTruthy();
        });
    });

    describe('promise-consistency', () => {

        test('set-result', async () => {
            const pcs1 = new PromiseCompletionSource<void>();
            const pcs2 = new PromiseCompletionSource<number>();
            const pcs3 = new PromiseCompletionSource<string>();

            const mockThen1 = jest.fn();
            const mockReject1 = jest.fn();

            const mockThen2 = jest.fn();
            const mockReject2 = jest.fn();

            const mockThen3 = jest.fn();
            const mockReject3 = jest.fn();

            expect(mockThen1).not.toHaveBeenCalled();
            expect(mockReject1).not.toHaveBeenCalled();

            expect(mockThen2).not.toHaveBeenCalled();
            expect(mockReject2).not.toHaveBeenCalled();

            expect(mockThen3).not.toHaveBeenCalled();
            expect(mockReject3).not.toHaveBeenCalled();

            pcs1.promise.then(mockThen1, mockReject1);
            pcs1.setResult(undefined);

            pcs2.promise.then(mockThen2, mockReject2);
            pcs2.setResult(123);

            pcs3.promise.then(mockThen3, mockReject3);
            pcs3.setException(new Error('foo'));

            expect(mockThen1).not.toHaveBeenCalled();
            expect(mockReject1).not.toHaveBeenCalled();

            expect(mockThen2).not.toHaveBeenCalled();
            expect(mockReject2).not.toHaveBeenCalled();

            expect(mockThen3).not.toHaveBeenCalled();
            expect(mockReject3).not.toHaveBeenCalled();

            await expect(pcs1.promise).resolves.toBe(undefined);
            await expect(pcs2.promise).resolves.toBe(123);
            await expect(pcs3.promise).rejects.toBeTruthy();

            expect(mockThen1).toHaveBeenCalledWith(undefined);
            expect(mockReject1).not.toHaveBeenCalled();

            expect(mockThen2).toHaveBeenCalledWith(123);
            expect(mockReject2).not.toHaveBeenCalled();

            expect(mockThen3).not.toHaveBeenCalled();
            expect(mockReject3).toHaveBeenCalled();
        });

    });

});

describe('PromiseHelper', () => {

    test('delay-works', async () => {
        const start = new Date().getTime();
        await PromiseHelper.delay(50);
        const stop = new Date().getTime();

        expect(stop - start).toBeGreaterThan(45);
        expect(stop - start).toBeLessThan(65);
    }, 100);

    test('delay-with-null-ct-works', async () => {

        await expect((PromiseHelper.delay as any).apply(null, [ 10, null ])).resolves.toBe(undefined);

    }, 100);

    test('delay-cancellation-works', async () => {

        const cts = new CancellationTokenSource();
        cts.cancelAfter(50);

        const start = new Date().getTime();
        let stop: number | null = null;
        let exception: any;

        try {
            try {
                const promise = PromiseHelper.delay(100, cts.token);
                promise.catch(jest.fn());
                await promise;
            } finally {
                stop = new Date().getTime();
            }
        } catch (ex) {
            exception = ex;
        }

        expect(exception).toBeInstanceOf(Error);

        if (!stop) {
            throw new Error('test setup is wrong');
        }

        expect(stop - start).toBeGreaterThan(35);
        expect(stop - start).toBeLessThan(100);
    }, 200);

    test('completedPromise-works', async () => {

        const start = new Date().getTime();
        await expect(PromiseHelper.completedPromise).resolves.toBe(undefined);
        const end = new Date().getTime();

        expect(end - start).toBeLessThan(10);

    }, 100);

    test('fromResult-works', async () => {

        const start = new Date().getTime();
        await expect(PromiseHelper.fromResult(123)).resolves.toBe(123);
        const end = new Date().getTime();

        expect(end - start).toBeLessThan(10);

    }, 100);

    test('fromException-works', async () => {

        const start = new Date().getTime();
        await expect(PromiseHelper.fromException<number>(new Error('foo'))).rejects.toBeInstanceOf(Error);
        const end = new Date().getTime();

        expect(end - start).toBeLessThan(10);

    }, 100);

    test('fromCanceled-works', async () => {

        const start = new Date().getTime();
        await expect(PromiseHelper.fromCanceled<number>()).rejects.toBeInstanceOf(Error);
        const end = new Date().getTime();

        expect(end - start).toBeLessThan(10);

    }, 100);

    test('whenAll-works-with-empty-list', async () => {

        await expect(PromiseHelper.whenAll()).resolves.toBeUndefined();

    }, 100);

    test('whenAll-works-with-already-completed-promises', async () => {
        await expect(PromiseHelper.whenAll(
            PromiseHelper.delay(0),
            PromiseHelper.delay(0, CancellationToken.default),
            PromiseHelper.fromResult(123),
            PromiseHelper.fromCanceled(),
            PromiseHelper.fromException(new Error('foo')),
            PromiseHelper.completedPromise
        )).resolves.toBeUndefined();
    }, 100);

    test('whenAll-works-normal', async () => {
        const mockThen = jest.fn();

        const pcs1 = new PromiseCompletionSource<number>();
        const pcs2 = new PromiseCompletionSource<number>();

        const whenAll = PromiseHelper.whenAll(pcs1.promise, pcs2.promise);
        whenAll.then(mockThen);

        expect(mockThen).not.toHaveBeenCalled();
        pcs1.setResult(123);
        expect(mockThen).not.toHaveBeenCalled();
        await PromiseHelper.delay(0);
        expect(mockThen).not.toHaveBeenCalled();

        pcs2.setCanceled();

        expect(mockThen).not.toHaveBeenCalled();
        await PromiseHelper.delay(0);
        expect(mockThen).toHaveBeenCalled();
    }, 100);

});
