import { Address, ConfigStore, ConnectHelper, ContractStore, TimeSpan } from '../../../src/std';
import { NamedPipeAddress } from '../../../src/node';

import { expect } from 'chai';
import { MockServiceProvider, _jsargs } from '../../infrastructure';
import { NodeAddressBuilder } from '../../../src/node/NodeAddressBuilder';

describe(`${ConfigStore.name}'s`, () => {
    class MockContract {}
    const mockAddress = new NamedPipeAddress('some-pipe');
    const mockTimeout = TimeSpan.fromSeconds(2);
    const mockServiceProvider = new MockServiceProvider<NodeAddressBuilder>({
        implementation: {
            contractStore: new ContractStore(),
        },
    });

    function createConfigStore(): ConfigStore<NodeAddressBuilder> {
        return new ConfigStore<NodeAddressBuilder>(mockServiceProvider);
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

        it(`should return the fallback ConnectHelper for any address`, () => {
            const expected: ConnectHelper<Address> = async context => {};

            const sut = createConfigStore();
            sut.setConnectHelper(undefined, expected);
            expect(sut.getConnectHelper(mockAddress)).to.equal(expected);
        });

        it(`should return the ConnectHelper that was configured for a particular address`, () => {
            const expected: ConnectHelper<Address> = async context => {};

            const sut = createConfigStore();
            sut.setConnectHelper(mockAddress, expected);
            expect(sut.getConnectHelper(mockAddress)).to.equal(expected);
        });
    });
});
