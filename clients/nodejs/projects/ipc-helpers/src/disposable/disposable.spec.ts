import { Disposable, IDisposable } from './disposable';

describe('Disposable', () => {

    test('combine-works', () => {
        expect(Disposable.combine()).toBe(Disposable.empty);
        expect(Disposable.combine(Disposable.empty)).toBe(Disposable.empty);

        const mockDisposable1: IDisposable = { dispose: jest.fn() };
        expect(Disposable.combine(mockDisposable1)).toBe(mockDisposable1);

        const mockDisposable2: IDisposable = { dispose: jest.fn() };
        let combined: IDisposable | null = null;
        expect(() => {
            combined = Disposable.combine(mockDisposable1, mockDisposable2);
        }).not.toThrow();

        expect(combined).toBeTruthy();

        // making the TypeScript compiler happy:
        const combined2 = combined as any as IDisposable;

        expect(mockDisposable1.dispose).not.toHaveBeenCalled();
        expect(mockDisposable2.dispose).not.toHaveBeenCalled();

        expect(() => combined2.dispose()).not.toThrow();

        expect(mockDisposable1.dispose).toHaveBeenCalled();
        expect(mockDisposable2.dispose).toHaveBeenCalled();
    });

});