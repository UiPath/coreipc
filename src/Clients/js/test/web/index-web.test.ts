import { expect } from 'chai';
import { ipc } from '../../src/web';

class IArithmetics {
    public Sum(x: number, y: number): Promise<number> {
        throw void 0;
    }
}

describe('web', () => {
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

    it('2 should equal 2', function () {
        expect(2).to.equal(2);
    });

    it('should connect', async () => {
        const url = 'ws://localhost:1234';
        const x = 2;
        const y = 3;
        const expected = 5;
        // const actual = 5;

        // await new Promise(resolve => {
        //     setTimeout(resolve, 1000);
        // });

        console.log(`🎂 Hello from the Browser!`);

        const proxy = ipc.proxy
            .withAddress((x) => x.isWebSocket(url))
            .withService(IArithmetics);

        const actual = await proxy.Sum(x, y);

        expect(actual).to.equal(expected);
    });
});
