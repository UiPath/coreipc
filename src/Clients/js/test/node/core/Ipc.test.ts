import { TimeSpan } from '../../../src/std';
import { ipc, IpcNodeImpl, NamedPipeAddress } from '../../../src/node';

import { expect } from 'chai';
import { _jsargs } from '../../infrastructure';

describe(`ipc`, () => {
    class MockContract {}
    const mockAddress = new NamedPipeAddress('some-pipe');
    const cMilliseconds = 2000;

    it('should work', () => {
        ipc.config.forAnyAddress().forAnyService().setRequestTimeout(cMilliseconds);
        const actual = (ipc as IpcNodeImpl).configStore.getRequestTimeout(
            mockAddress,
            MockContract,
        );

        expect(actual).to.not.be.undefined;

        actual!.should.be
            .instanceOf(TimeSpan)
            .and.have.property('totalMilliseconds')
            .equal(cMilliseconds);
    });
});
