import { Address, ConfigStore, ServiceId } from '../../../src/std';
import { NodeWebSocketAddress } from '../../../src/node';

import { expect } from 'chai';
import { cover, fcover } from '../../infrastructure';

cover<typeof ConfigStore>(ConfigStore, function () {
    this.coverGetBuilder(function () {
        class MockContract {}

        const serviceId1 = new ServiceId(MockContract);
        const serviceId2 = new ServiceId(MockContract, 'endpoint');
        const address = new NodeWebSocketAddress('ws://localhost:61234');

        const builder = this.whenCalled(
            this(undefined, serviceId1),
            this(undefined, serviceId2),
            this(undefined, undefined),
            this(undefined),
            this(address),
            this(address, serviceId1),
            this(address, serviceId2),
            this(undefined, serviceId1),
            this(undefined, serviceId2),
        );

        this._should('be pure 2', () => {
            const sut = new ConfigStore();

            sut.getBuilder(undefined, undefined).should.equal(
                sut.getBuilder(undefined, undefined),
            );
        });

        builder.shouldAll('not throw', call => {
            const sut = new ConfigStore();

            const act = () => sut.getBuilder(...call.args);

            act.should.not.throw();
        });

        builder.shouldAll('be a pure function', call => {
            const sut = new ConfigStore();

            const first = sut.getBuilder(...call.args);
            const second = sut.getBuilder(...call.args);

            first.should.equal(second);
        });
    });

    this.coverConstructor(function () {
        const construction = this();
        const x = construction();

        this._should('not throw', () => {
            const act = this();

            act.should.not.throw();
        });
    });
});

describe(`${ConfigStore.name}'s`, () => {
    describe(`ctor`, () => {
        it(`should not throw`, () => {
            const act = () => new ConfigStore();
            expect(act).not.to.throw();
        });
    });

    describe(`📞 "getBuilder"`, () => {
        class MockContract {}

        function applyTestCases(
            theory: (
                args: Parameters<typeof ConfigStore.prototype.getBuilder>,
            ) => void,
        ) {
            theory([undefined, new ServiceId(MockContract)]);
            theory([
                undefined,
                new ServiceId(MockContract, 'some-endpoint-name'),
            ]);
            theory([undefined, undefined]);
            theory([undefined]);
            theory([]);
            theory([new NodeWebSocketAddress('ws://localhost:61234')]);
            theory([
                new NodeWebSocketAddress('ws://localhost:61234'),
                new ServiceId(MockContract),
            ]);
            theory([
                new NodeWebSocketAddress('ws://localhost:61234'),
                new ServiceId(MockContract, 'some-endpoint-name'),
            ]);
        }

        describe(`🚫 should not throw when called with valid args`, () => {
            let sut: ConfigStore = undefined!;

            beforeEach(() => {
                sut = new ConfigStore();
            });

            const theory = (
                args: Parameters<typeof ConfigStore.prototype.getBuilder>,
            ) => {
                it(`🌿 args === ${JSON.stringify(args)}`, () => {
                    const act = () => sut.getBuilder(...args);

                    expect(act).not.to.throw();
                });
            };

            applyTestCases(theory);
        });

        describe(`📦 should be a pure function`, () => {
            let sut: ConfigStore = undefined!;

            beforeEach(() => {
                sut = new ConfigStore();
            });

            const theory = (
                args: Parameters<typeof ConfigStore.prototype.getBuilder>,
            ) => {
                it(`🌿 args === ${JSON.stringify(args)}`, () => {
                    const firstResult = sut.getBuilder(...args);
                    const secondResult = sut.getBuilder(...args);

                    expect(firstResult).to.equal(secondResult);
                });
            };

            applyTestCases(theory);
        });
    });
});
