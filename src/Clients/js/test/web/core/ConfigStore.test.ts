import { Address, ConfigStore, ConnectHelper, ContractStore, TimeSpan } from '../../../src/std';
import { BrowserWebSocketAddress, WebAddressBuilder } from '../../../src/web';
import { expect } from 'chai';
import { MockServiceProvider } from '../../infrastructure';

describe(`${ConfigStore.name}'s`, () => {
    class MockContract {}
    const mockAddress = new BrowserWebSocketAddress('ws://localhost:61234');
    const mockTimeout = TimeSpan.fromSeconds(2);
    const mockServiceProvider = new MockServiceProvider<WebAddressBuilder>({
        implementation: {
            contractStore: new ContractStore(),
        },
    });

    function createConfigStore(): ConfigStore<WebAddressBuilder> {
        return new ConfigStore<WebAddressBuilder>(mockServiceProvider);
    }

    describe(`ctor`, () => {
        it(`should not throw`, () => {
            const act = createConfigStore;
            expect(act).not.to.throw();
        });
    });

    describe(`📞 "${ConfigStore.prototype.getRequestTimeout.name}"`, () => {
        it(`should return undefined by default`, () => {
            const sut = createConfigStore();
            expect(sut.getRequestTimeout(mockAddress, MockContract)).to.equal(undefined);
        });

        it(`should return whatever was configured`, () => {
            const sut = createConfigStore();
            sut.setRequestTimeout(undefined, undefined, mockTimeout);
            expect(sut.getRequestTimeout(mockAddress, MockContract)).to.deep.eq(mockTimeout);
        });
    });

    describe(`📞 "${ConfigStore.prototype.getConnectHelper.name}"`, () => {
        it(`should return undefined by default`, () => {
            const sut = createConfigStore();
            const actual = sut.getConnectHelper(mockAddress);
            expect(actual).to.equal(undefined);
        });

        it(`should return whatever was configured`, () => {
            const expected: ConnectHelper<Address> = async context => {};

            const sut = createConfigStore();
            sut.setConnectHelper(undefined, expected);
            expect(sut.getConnectHelper(mockAddress)).to.equal(expected);
        });
    });
});
