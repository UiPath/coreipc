import { expect } from 'chai';
import { Message, PromisePal } from '../../src/std';
import { ipc } from '../../src/web';
import { IAlgebra, IArithmetics } from './Contracts';
import { algebraProxyFactory, serverUrl } from './Fixtures';

describe('web:the browser built-in WebSocket class', () => {
    it('should be instantiable', () => {
        const act = () =>
            eval(/* js */ `
            new WebSocket('ws://localhost:61234', 'foobar')
        `);
        expect(act).not.to.throw().and.to.be.instanceOf(WebSocket);
    });
});

describe('web:end-to-end', () => {
    describe('remote procedure calling', () => {
        let algebraProxy: IAlgebra = null!;

        beforeAll(() => {
            algebraProxy = algebraProxyFactory();
        });

        it('should work', async () => {
            const x = 2;
            const y = 3;
            const expected = 6;

            const actual = await algebraProxy.MultiplySimple(x, y);

            expect(actual).to.equal(expected);
        });

        it('should work concurrently', async () => {
            const span1 = 500;
            const span2 = 1;

            let call1Completed = false;

            async function call1() {
                try {
                    await algebraProxy.Sleep(span1);
                } finally {
                    call1Completed = true;
                }
            }

            const call1Wrapper = call1();

            await PromisePal.delay(1);
            await algebraProxy.Sleep(span2);
            expect(call1Completed).to.equal(false);
            await call1Wrapper;
        });

        it('should work with callbacks', async () => {
            const x = 7;
            const originalMessage = new Message<number>({ payload: x });
            const expected = true;

            let receivedMessage: Message<number> | undefined;

            const arithmetics = new (class implements IArithmetics {
                Sum(x: number, y: number): Promise<number> {
                    throw new Error('Method not implemented.');
                }
                async SendMessage(message: Message<number>): Promise<boolean> {
                    receivedMessage = message;
                    return true;
                }
            })();

            ipc.callback
                .forAddress(x => x.isWebSocket(serverUrl))
                .forService<IArithmetics>('IArithmetics')
                .is(arithmetics);

            const actual = await algebraProxy.TestMessage(originalMessage);

            expect(actual).to.equal(expected);
            expect(receivedMessage).not.to.be.undefined.and.not.to.be.null;
            expect(receivedMessage?.Payload).to.equal(originalMessage.Payload);
        });
    });
});
