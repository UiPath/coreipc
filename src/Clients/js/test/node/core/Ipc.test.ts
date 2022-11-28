import * as std from '../../../src/std';
import { ipc, Ipc, NamedPipeAddress } from '../../../src/node';

import { expect } from 'chai';
import { cover, _jsargs } from '../../infrastructure';

const $Ipc = cover<typeof Ipc>(Ipc);

describe($Ipc, () => {
    class MockContract {}
    const mockAddress = new NamedPipeAddress('some-pipe');
    const cMilliseconds = 2000;

    it('should work', () => {
        ipc.config
            .forAnyAddress()
            .forAnyService()
            .setRequestTimeout(cMilliseconds);
        const actual = ipc.configStore.getRequestTimeout(
            mockAddress,
            std.ServiceId.from(MockContract),
        );

        expect(actual).to.not.be.undefined;

        actual!.should.be
            .instanceOf(std.TimeSpan)
            .and.have.property('totalMilliseconds')
            .equal(cMilliseconds);
    });
});
