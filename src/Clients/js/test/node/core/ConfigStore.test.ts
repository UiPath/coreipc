import { Address, ConfigStore, ServiceId } from '../../../src/std';
import { NodeWebSocketAddress } from '../../../src/node';

import { expect } from 'chai';

describe(`${ConfigStore.name}'s`, () => {

    describe(`ctor`, () => {

        it(`should not throw`, () => {
            const act = () => new ConfigStore();
            expect(act).not.to.throw();
        });

    });

    describe(`📞 "getBuilder"`, () => {

        class MockContract { }

        function applyTestCases(theory: (args: Parameters<typeof ConfigStore.prototype.getBuilder>) => void) {
            theory([undefined, new ServiceId(MockContract)]);
            theory([undefined, new ServiceId(MockContract, 'some-endpoint-name')]);
            theory([undefined, undefined]);
            theory([undefined]);
            theory([]);
            theory([new NodeWebSocketAddress('ws://localhost:61234')]);
            theory([new NodeWebSocketAddress('ws://localhost:61234'), new ServiceId(MockContract)]);
            theory([new NodeWebSocketAddress('ws://localhost:61234'), new ServiceId(MockContract, 'some-endpoint-name')]);
        }

        describe(`🚫 should not throw when called with valid args`, () => {
            let sut: ConfigStore = undefined!;

            beforeEach(() => {
                sut = new ConfigStore();
            });

            const theory = (args: Parameters<typeof ConfigStore.prototype.getBuilder>) => {
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

            const theory = (args: Parameters<typeof ConfigStore.prototype.getBuilder>) => {
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
