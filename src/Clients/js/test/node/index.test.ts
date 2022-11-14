import { ipc } from '../../src/node';
import { expect } from 'chai';

class IArithmetics {
    sumAsync(x: number, y: number): Promise<number> { throw void 0; }
}

describe('node', function() {

    it('should work', async () => {
        const url = 'ws://foobar';
        const pipeName = 'some-pipe';

        const proxy1 = ipc
            .proxy
            .withWebSocket(url)
            .withService(IArithmetics)
            ;

        const proxy2 = ipc
            .proxy
            .withNamedPipe(pipeName)
            .withService(IArithmetics)
            ;

        expect((proxy1 as any).address.url).to.equal(url);
        expect((proxy2 as any).address.pipeName).to.equal(pipeName);
    });

});

