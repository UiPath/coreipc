import {
    AggregateDisposable,
    AggregateError,
    IDisposable,
} from '../../../src/std';

import { expect } from 'chai';
import { cover } from '../../infrastructure';

const $AggregateDisposable =
    cover<typeof AggregateDisposable>(AggregateDisposable);

describe($AggregateDisposable.$Constructor, () => {
    describe($AggregateDisposable.$Dispose, () => {
        it(`should not throw`, () => {
            const mockDisposable = { dispose(): void {} };
            const sut = new AggregateDisposable(mockDisposable);
            const act = () => sut.dispose();

            expect(act).not.to.throw();
        });
    });
});

describe(`${AggregateDisposable.name}'s`, () => {
    class Mock implements IDisposable {
        public get invocationCount() {
            return this._invocationCount;
        }

        dispose(): void {
            this._invocationCount++;
        }
        toString() {
            return `new ${Mock.name}()`;
        }

        private _invocationCount = 0;
    }

    describe(`ctor`, () => {
        describe(`should not throw when called with valid args`, () => {
            const argsList = [
                [new Mock()],
                [new Mock(), new Mock()],
                [new Mock(), new Mock(), new Mock()],
            ] as ConstructorParameters<typeof AggregateDisposable>[];

            for (const args of argsList) {
                const argsToString = JSON.stringify(args.map(x => `${x}`));

                it(`when called with ${argsToString}`, () => {
                    const act = () => new AggregateDisposable(...args);
                    expect(act).not.to.throw();
                });
            }
        });

        describe(`should throw when called with invalid args`, () => {
            const argsList = [
                [],
                [123],
                [true],
                [new Mock(), 123],
                [new Mock(), {}],
                [new Mock(), new Mock(), true],
            ] as ConstructorParameters<typeof AggregateDisposable>[];

            for (const args of argsList) {
                const argsToString = JSON.stringify(args.map(x => `${x}`));

                it(`when called with ${argsToString}`, () => {
                    const act = () => new AggregateDisposable(...args);
                    expect(act).to.throw();
                });
            }
        });
    });

    describe(`📞 "maybeCreate" static method`, () => {
        it(`should return null when called with no args`, () => {
            expect(AggregateDisposable.maybeCreate()).to.equal(null);
        });

        it(`should return an ${AggregateDisposable.name} instance when called with at least one arg`, () => {
            let x: AggregateDisposable | null | undefined = undefined;
            x = AggregateDisposable.maybeCreate({ dispose() {} });
            try {
                expect(x).to.be.instanceOf(AggregateDisposable);
            } finally {
                try {
                    x?.dispose();
                } catch {}
            }
        });
    });

    describe(`📞 "dispose" instance method`, () => {
        it(`should dispose all the aggregate disposables`, () => {
            const mock1 = new Mock();
            const mock2 = new Mock();

            const sut = new AggregateDisposable(mock1, mock2);

            expect(mock1.invocationCount).to.equal(0);
            expect(mock2.invocationCount).to.equal(0);

            sut.dispose();

            expect(mock1.invocationCount).to.equal(1);
            expect(mock2.invocationCount).to.equal(1);
        });

        it(`should reinvoke the disposal of all the aggregate disposables each additional time it's called`, () => {
            const mock1 = new Mock();
            const mock2 = new Mock();

            const sut = new AggregateDisposable(mock1, mock2);

            expect(mock1.invocationCount).to.equal(0);
            expect(mock2.invocationCount).to.equal(0);

            sut.dispose();
            sut.dispose();
            sut.dispose();

            expect(mock1.invocationCount).to.equal(3);
            expect(mock2.invocationCount).to.equal(3);
        });

        it(`should aggregate and throw any errors thrown by the aggregated disposables`, () => {
            const error1 = new Error();
            const error2 = new Error();

            const mock1 = {
                dispose() {
                    throw error1;
                },
            };

            const mock2 = {
                dispose() {
                    throw error2;
                },
            };

            const sut = new AggregateDisposable(mock1, mock2);

            const act = () => sut.dispose();

            expect(act)
                .to.throw(AggregateError)
                .that.satisfies((x: any) => {
                    expect(x).to.be.instanceOf(AggregateError);
                    const specific = x as AggregateError;

                    expect(specific.errors)
                        .to.have.lengthOf(2)
                        .which.contains(error1)
                        .and.contains(error2);

                    return true;
                });
        });
    });
});
