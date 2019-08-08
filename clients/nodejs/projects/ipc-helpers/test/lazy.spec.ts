import { Lazy } from '../src/helpers/lazy';

describe('Lazy<T>', () => {

    test('just-works', () => {

        const expectedValue = 'some value';
        const mockFactory = jest.fn(() => expectedValue);

        let lazy: Lazy<string> | null = null;

        expect(() => new Lazy<string>(null as any)).toThrow('');
        expect(() => { lazy = new Lazy<string>(mockFactory); }).not.toThrow();
        expect(lazy).toBeInstanceOf(Lazy);

        const makingTypeScriptHappy = lazy as any as Lazy<string>;

        expect(makingTypeScriptHappy.isValueCreated).toBeFalsy();
        expect(mockFactory).not.toHaveBeenCalled();

        expect(() => makingTypeScriptHappy.value).not.toThrow();
        expect(makingTypeScriptHappy.value).toBe(expectedValue);
        expect(makingTypeScriptHappy.value).toBe(makingTypeScriptHappy.value);

        expect(mockFactory).toHaveBeenCalled();
        expect(makingTypeScriptHappy.isValueCreated).toBeTruthy();
    });

});
