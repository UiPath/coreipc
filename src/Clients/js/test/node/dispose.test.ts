import { expect } from 'chai';
import { ipc } from '../../src/node';
import { IAlgebra } from './Contracts';
import { AddressHelper as TestContext } from './Fixtures';

describe('node:dispose', () => {
    for (const context of [TestContext.WebSocket, TestContext.NamedPipe]) {

        describe(`dispose with context: ${context}`, () => {
            let algebraProxy: IAlgebra = null!;

            beforeAll(() => {
                algebraProxy = ipc.proxy.withAddress(context.address).withService(IAlgebra);
            });

            it('should work after disposeChannel', async () => {
                // Make a call to establish a channel
                const first = await algebraProxy.MultiplySimple(3, 4);
                expect(first).to.equal(12);

                // Dispose the channel
                await ipc.disposeChannel(context.address);

                // Make another call — should reconnect automatically
                const second = await algebraProxy.MultiplySimple(5, 6);
                expect(second).to.equal(30);
            });

            it('should work after disposeAllChannels', async () => {
                const first = await algebraProxy.MultiplySimple(2, 7);
                expect(first).to.equal(14);

                await ipc.disposeAllChannels();

                const second = await algebraProxy.MultiplySimple(3, 8);
                expect(second).to.equal(24);
            });

            it('should survive multiple dispose-reconnect cycles', async () => {
                for (let i = 1; i <= 3; i++) {
                    const result = await algebraProxy.MultiplySimple(i, 10);
                    expect(result).to.equal(i * 10);

                    await ipc.disposeAllChannels();
                }

                // One final call after the last dispose
                const final = await algebraProxy.MultiplySimple(9, 9);
                expect(final).to.equal(81);
            });

            it('disposeChannel should be a no-op for an address with no open channel', async () => {
                await ipc.disposeChannel(context.address);

                // Subsequent call should still work
                const result = await algebraProxy.MultiplySimple(4, 4);
                expect(result).to.equal(16);
            });

            it('disposeAllChannels should be a no-op when no channels are open', async () => {
                await ipc.disposeAllChannels();
                await ipc.disposeAllChannels();

                const result = await algebraProxy.MultiplySimple(6, 7);
                expect(result).to.equal(42);
            });
        });
    }
});
