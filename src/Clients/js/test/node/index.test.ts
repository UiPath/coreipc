import { ipc } from '../../src/node';
import { expect } from 'chai';

class IAlgebra {
    public MultiplySimple(x: number, y: number): Promise<number> {
        throw void 0;
    }
}

describe('node', function () {
    // beforeAll(() => {
    //     console.log(`🎂 BEFORE ALL`)
    // });

    // afterAll(()=>{
    //     console.log(`🎂 AFTER ALL`)
    // });

    it('test 1', async () => {
        // let proxy: IAlgebra | undefined = undefined;

        // const act = () => {
        //     proxy = ipc.proxy
        //         .withAddress(builder => builder.isWebSocket('ws://localhost:1234'))
        //         .withService(IAlgebra);
        // };

        // expect(act).not.to.throw();
        // expect(proxy!).to.be.instanceOf(Object);

        // ipc.config
        //     .forAddress(x => x.isWebSocket('ws://localhost:1234'))
        //     .forAnyService()
        //     .update(builder =>
        //         builder
        //             .setConnectHelper(async context => {
        //                 await context.tryConnectAsync();
        //             })
        //             .setRequestTimeout(100),
        //     );

        // const actual = await proxy!.MultiplySimple(2, 3);
        // expect(actual).to.equal(6);
    });
});
