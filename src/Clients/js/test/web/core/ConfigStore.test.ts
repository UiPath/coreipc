import {
    Address,
    ConfigStore,
    ConnectHelper,
    ServiceId,
    TimeSpan,
} from '../../../src/std';
import { BrowserWebSocketAddress } from '../../../src/web';

import { expect } from 'chai';

describe(`${ConfigStore.name}'s`, () => {
    class MockContract {}
    const mockAddress = new BrowserWebSocketAddress('ws://localhost:61234');
    const mockTimeout = TimeSpan.fromSeconds(2);

    describe(`ctor`, () => {
        it(`should not throw`, () => {
            const act = () => new ConfigStore();
            expect(act).not.to.throw();
        });
    });

    describe(`📞 "${ConfigStore.prototype.getRequestTimeout.name}"`, () => {
        it(`should return undefined by default`, () => {
            const sut = new ConfigStore();
            expect(
                sut.getRequestTimeout(
                    mockAddress,
                    ServiceId.from(MockContract),
                ),
            ).to.equal(undefined);
        });

        it(`should return whatever was configured`, () => {
            const sut = new ConfigStore();
            sut.setRequestTimeout(undefined, undefined, mockTimeout);
            expect(
                sut.getRequestTimeout(
                    mockAddress,
                    ServiceId.from(MockContract),
                ),
            ).to.deep.eq(mockTimeout);
        });
    });

    describe(`📞 "${ConfigStore.prototype.getConnectHelper.name}"`, () => {
        it(`should return undefined by default`, () => {
            const sut = new ConfigStore();
            const actual = sut.getConnectHelper(mockAddress);
            expect(actual).to.equal(undefined);
        });

        it(`should return whatever was configured`, () => {
            const expected: ConnectHelper<Address> = async context => {};

            const sut = new ConfigStore();
            sut.setConnectHelper(undefined, expected);
            expect(sut.getConnectHelper(mockAddress)).to.equal(expected);
        });
    });
});
