import { ArgumentErrorBase } from '../../src/std';

export function __mentionsParameter(name: string) {
    return (x: any) => {
        expect(x).toBeInstanceOf(ArgumentErrorBase);

        const specific = x as ArgumentErrorBase;

        expect(specific.paramName)
            .withContext(`The ${ArgumentErrorBase.name}'s paramName`)
            .toBe(name);

        return true;
    };
}
