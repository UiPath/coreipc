import { expect } from 'chai';
import 'mocha';
import { Quack } from '@foundation/utils';
import { InvalidOperationError } from '@foundation/errors';

describe(`foundation:utils -> class:Quack`, () => {
    context(`ctor`, async () => {
        it(`shouldn't throw`, () => {
            expect(() => new Quack<string>()).not.to.throw();
        });
    });

    context(`method:push`, () => {
        it(`shouldn't throw`, () => {
            const quack = new Quack<string>();
            expect(() => quack.push('foo')).not.to.throw();
        });
    });

    context(`method:enqueue`, () => {
        it(`shouldn't throw`, () => {
            const quack = new Quack<string>();
            expect(() => quack.push('foo')).not.to.throw();
        });
    });

    context(`method:pop`, () => {
        it(`shouldn't throw if the quack is not empty`, () => {
            const quack = new Quack<string>();
            quack.push('foo');
            expect(() => quack.pop()).not.to.throw();
        });
        it(`should throw if the quack is empty`, () => {
            const quack = new Quack<string>();
            expect(() => quack.pop()).to.throw(InvalidOperationError, 'Quack is empty.');
        });
    });

    it(`should be consistent`, () => {
        const quack = new Quack<string>();

        expect(quack.length).to.equal(0);
        expect(quack.any).to.equal(false);
        expect(quack.empty).to.equal(true);
        expect(() => quack.pop()).to.throw(InvalidOperationError, 'Quack is empty.');

        quack.push('item-1');
        expect(quack.length).to.equal(1);
        expect(quack.any).to.equal(true);
        expect(quack.empty).to.equal(false);

        quack.push('item-2');
        expect(quack.length).to.equal(2);
        expect(quack.any).to.equal(true);
        expect(quack.empty).to.equal(false);

        for (const index of Array(100).keys()) {
            quack.push(`item-${index + 3}`);
        }
        expect(quack.length).to.eq(102);
        expect(quack.any).to.equal(true);
        expect(quack.empty).to.equal(false);

        quack.enqueue('item-0');
        expect(quack.length).to.equal(103);
        expect(quack.any).to.equal(true);
        expect(quack.empty).to.equal(false);

        expect(quack.pop()).to.equal('item-102');
        expect(quack.length).to.equal(102);
        expect(quack.any).to.equal(true);
        expect(quack.empty).to.equal(false);

        let expected = 101;
        while (quack.any) {
            expect(quack.pop()).to.equal(`item-${expected--}`);
        }
        expect(quack.length).to.equal(0);
        expect(quack.empty).to.equal(true);
        expect(() => quack.pop()).to.throw(InvalidOperationError, 'Quack is empty.');
    });
});
