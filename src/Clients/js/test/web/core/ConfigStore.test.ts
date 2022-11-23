import { Address, ConfigStore, ServiceId } from '../../../src/std';
import { BrowserWebSocketAddress } from '../../../src/web';

import { expect } from 'chai';

describe(`${ConfigStore.name}'s`, () => {

    describe(`ctor`, () => {

        it(`should not throw`, () => {
            const act = () => new ConfigStore();
            expect(act).not.to.throw();
        });

    });

    describe(`📞 "getBuilder"`, () => {

        describe(`🚫 should throw when called with invalid args`, () => {
            let sut: ConfigStore = undefined!;

            beforeEach(() => {
                sut = new ConfigStore();
            });

            const theory = (args: Parameters<typeof ConfigStore.prototype.getBuilder>) => {
                it(`🌿 args === ${JSON.stringify(args)}`, () => {

                    const act = () => sut.getBuilder(...args);

                    expect(act).to.throw();

                });
            };

            class MockContract { }

            theory([123 as any, new ServiceId(MockContract)]);
            theory([123 as any, undefined]);
            theory([undefined, 123 as any]);
            theory([undefined, 'foo' as any]);
            theory([new BrowserWebSocketAddress('ws://localhost:61234'), 'foo' as any]);

        });

    });
});
