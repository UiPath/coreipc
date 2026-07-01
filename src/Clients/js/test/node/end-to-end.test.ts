import { expect } from 'chai';
import {
    CancellationTokenSource,
    Message,
    OperationCanceledError,
    PromisePal,
} from '../../src/std';
import { ipc } from '../../src/node';
import { IAlgebra, IArithmetic } from './Contracts';
import { AddressHelper as TestContext } from './Fixtures';

async function waitFor(
    predicate: () => Promise<boolean>,
    timeoutMs: number,
    intervalMs = 100,
): Promise<boolean> {
    const deadline = Date.now() + timeoutMs;
    do {
        if (await predicate()) {
            return true;
        }
        await PromisePal.delay(intervalMs);
    } while (Date.now() < deadline);
    return false;
}

describe('node:end-to-end', () => {
    for (const context of [TestContext.WebSocket, TestContext.NamedPipe]) {

        describe(`RPC with context: ${context}`, () => {
            let algebraProxy: IAlgebra = null!;

            beforeAll(() => {
                try {
                    algebraProxy = ipc.proxy.withAddress(context.address).withService(IAlgebra);
                } catch (err) {
                    console.error(err);
                    throw err;
                }
            });

            beforeEach(() => {
                (jasmine as any).getEnv().defaultTimeoutInterval = jasmine.DEFAULT_TIMEOUT_INTERVAL = 60 * 60 * 1000;
            });

            it('should work', async () => {
                const x = 2;
                const y = 3;
                const expected = 6;

                const actual = await algebraProxy.MultiplySimple(x, y);

                expect(actual).to.equal(expected);
            }, 60 * 60 * 1000);

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

                const arithmetic = new (class implements IArithmetic {
                    Sum(x: number, y: number): Promise<number> {
                        throw new Error('Method not implemented.');
                    }
                    async SendMessage(message: Message<number>): Promise<boolean> {
                        receivedMessage = message;
                        return true;
                    }
                })();

                ipc.callback
                    .forAddress(context.address)
                    .forService<IArithmetic>('IArithmetic')
                    .is(arithmetic);

                const actual = await algebraProxy.TestMessage(originalMessage);

                expect(actual).to.equal(expected);
                expect(receivedMessage).not.to.be.undefined.and.not.to.be.null;
                expect(receivedMessage?.Payload).to.equal(originalMessage.Payload);
            });

            it('propagates caller-side cancellation to the .NET callee', async () => {
                // The .NET Algebra server is a shared singleton across the
                // WebSocket/NamedPipe runs, so assert a delta rather than an
                // absolute count.
                const before = await algebraProxy.CancellationCount();

                const cts = new CancellationTokenSource();
                const local = algebraProxy
                    .WaitForCancellation(cts.token)
                    .then(() => 'resolved' as const, (err: unknown) => err);

                // Let the request reach the server and park on its token.
                await PromisePal.delay(100);

                cts.cancel();

                // The caller's own promise rejects locally with a cancellation...
                const outcome = await local;
                expect(outcome).to.be.instanceOf(OperationCanceledError);

                // ...and the .NET handler's injected CancellationToken fires — which
                // can only happen if the TS client actually transmitted a
                // CancellationRequest frame to the server.
                const serverObserved = await waitFor(
                    async () => (await algebraProxy.CancellationCount()) === before + 1,
                    10_000,
                );
                expect(
                    serverObserved,
                    'the .NET server never observed the cancellation',
                ).to.equal(true);
            }, 30_000);

        });
    }
});
