import { ipc } from '../../src/node';
import { expect } from 'chai';

class IArithmetics {
    sumAsync(x: number, y: number): Promise<number> {
        throw void 0;
    }
}

describe('node', function () {
    it('should work 2', async () => {
        let arithmetics: IArithmetics | undefined = undefined;

        const act = () => {
            arithmetics = ipc.proxy
                .withAddress((builder) => builder.isWebSocket('ws://foobar'))
                .withService(IArithmetics);
        };

        expect(act).not.to.throw();
        expect(arithmetics!).to.be.instanceOf(Object);

        ipc.config
            .forAddress((x) => x.isWebSocket('ws://foobar.com'))
            .forAnyService()
            .update((builder) =>
                builder
                    .setConnectHelper(async (context) => {
                        const x = context.address.url;
                    })
                    .setRequestTimeout(100)
            );

        const actual = await arithmetics!.sumAsync(2, 3);
        expect(actual).to.equal(5);
    });
});
