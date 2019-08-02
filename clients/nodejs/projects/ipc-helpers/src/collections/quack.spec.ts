import { Quack } from './quack';

describe('Quack<T>', () => {

    test('ctor-doesnt-throw', () => {
        expect(() => new Quack<number>()).not.toThrow();
        expect(() => new Quack<number>(1, 2, 3)).not.toThrow();
    });

    test('general-consistency', () => {
        let quack = new Quack<number>();
        expect(quack.count).toBe(0);
        expect(quack.empty).toBeTruthy();
        expect(quack.any).toBeFalsy();
        expect(quack.tryDequeue().success).toBeFalsy();
        expect(quack.tryPeek().success).toBeFalsy();
        expect(() => quack.dequeue()).toThrow();
        expect(() => quack.peek()).toThrow();

        quack.enqueue(1);
        expect(quack.count).toBe(1);
        expect(quack.empty).toBeFalsy();
        expect(quack.any).toBeTruthy();
        expect(quack.tryDequeue().success).toBeTruthy();
        quack.pushFront(1);
        expect(quack.tryDequeue().item).toBe(1);
        quack.pushFront(1);
        expect(quack.tryPeek().success).toBeTruthy();
        expect(quack.tryPeek().item).toBe(1);
        expect(quack.dequeue()).toBe(1);
        quack.pushFront(1);
        expect(quack.peek()).toBe(1);

        quack.pushFront(2);
        expect(quack.count).toBe(2);
        expect(quack.empty).toBeFalsy();
        expect(quack.any).toBeTruthy();

        quack.enqueue(3);
        expect(quack.count).toBe(3);
        expect(quack.empty).toBeFalsy();
        expect(quack.any).toBeTruthy();

        quack.enqueue(4);
        expect(quack.count).toBe(4);
        expect(quack.empty).toBeFalsy();
        expect(quack.any).toBeTruthy();

        expect(quack.dequeue()).toBe(2);
        expect(quack.dequeue()).toBe(1);
        expect(quack.dequeue()).toBe(3);
        expect(quack.dequeue()).toBe(4);

        expect(quack.count).toBe(0);
        expect(quack.empty).toBeTruthy();
        expect(quack.any).toBeFalsy();

        quack = new Quack<number>(1, 2, 3, 4, 5);
        expect(quack.count).toBe(5);
        expect(quack.empty).toBeFalsy();
        expect(quack.any).toBeTruthy();

        expect(quack.dequeue()).toBe(1);
        expect(quack.dequeue()).toBe(2);
        expect(quack.dequeue()).toBe(3);
        expect(quack.dequeue()).toBe(4);
        expect(quack.dequeue()).toBe(5);

        expect(quack.count).toBe(0);
        expect(quack.empty).toBeTruthy();
        expect(quack.any).toBeFalsy();

        expect(quack.tryDequeue().success).toBeFalsy();
        expect(quack.tryPeek().success).toBeFalsy();
        expect(() => quack.dequeue()).toThrow();
        expect(() => quack.peek()).toThrow();
    });

});
