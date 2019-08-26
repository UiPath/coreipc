// tslint:disable: align
// tslint:disable: ban-types
import { Quack } from '../../src/foundation/data-structures/quack';
import { InvalidOperationError } from '../../src/foundation/errors/invalid-operation-error';
import '../custom-matchers';

describe('Foundation-Quack', () => {

    test(`ctor doesn't throw`, () => {
        expect(() => new Quack<number>()).not.toThrow();
    });
    test(`enqueue doesn't throw`, () => {
        const quack = new Quack<number>();
        expect(() => quack.enqueue(0)).not.toThrow();
    });
    test(`push doesn't throw`, () => {
        const quack = new Quack<number>();
        expect(() => quack.push(0)).not.toThrow();
    });
    test(`length doesn't throw`, () => {
        const quack = new Quack<number>();
        expect(() => quack.length).not.toThrow();
    });
    test(`any doesn't throw`, () => {
        const quack = new Quack<number>();
        expect(() => quack.any).not.toThrow();
    });
    test(`empty doesn't throw`, () => {
        const quack = new Quack<number>();
        expect(() => quack.empty).not.toThrow();
    });

    test(`quack is consistent`, () => {
        const quack = new Quack<number>();
        /*               */ expect(quack.any).toBe(false);
        /*               */ expect(quack.empty).toBe(true);
        /*               */ expect(quack.length).toBe(0);
        expect(() => quack.pop()).toThrowPrecisely(InvalidOperationError);

        quack.push(0);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(1);

        quack.push(1);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(2);

        quack.push(2);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(3);

        expect(quack.pop()).toBe(2);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(2);

        expect(quack.pop()).toBe(1);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(1);

        expect(quack.pop()).toBe(0);
        /*               */ expect(quack.any).toBe(false);
        /*               */ expect(quack.empty).toBe(true);
        /*               */ expect(quack.length).toBe(0);

        expect(() => quack.pop()).toThrowPrecisely(InvalidOperationError);

        quack.enqueue(0);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(1);

        quack.enqueue(1);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(2);

        quack.enqueue(2);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(3);

        expect(quack.pop()).toBe(0);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(2);

        expect(quack.pop()).toBe(1);
        /*               */ expect(quack.any).toBe(true);
        /*               */ expect(quack.empty).toBe(false);
        /*               */ expect(quack.length).toBe(1);

        expect(quack.pop()).toBe(2);
        /*               */ expect(quack.any).toBe(false);
        /*               */ expect(quack.empty).toBe(true);
        /*               */ expect(quack.length).toBe(0);

        expect(() => quack.pop()).toThrowPrecisely(InvalidOperationError);
    });

});
