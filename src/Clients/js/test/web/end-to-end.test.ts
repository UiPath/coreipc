import { expect } from 'chai';
import { ipc } from '../../src/web';

class IAlgebra {
    public MultiplySimple(x: number, y: number): Promise<number> {
        throw void 0;
    }
}

describe('web:end-to-end', () => {
    beforeEach(function () {
        if (global.process?.versions?.node) {
            this.skip();
        }
    });

    it('new WebSocket should not throw', function () {
        const act = () =>
            eval(/* js */ `
            new WebSocket('ws://localhost:1234', 'foobar')
        `);
        expect(act).not.to.throw();
    });

    it('calling MultiplySimple(2, 3) should return 6', async () => {
        const url = 'ws://localhost:1234';
        const x = 2;
        const y = 3;
        const expected = 6;

        const proxy = ipc.proxy.withAddress(x => x.isWebSocket(url)).withService(IAlgebra);

        const actual = await proxy.MultiplySimple(x, y);

        expect(actual).to.equal(expected);
    });
});
