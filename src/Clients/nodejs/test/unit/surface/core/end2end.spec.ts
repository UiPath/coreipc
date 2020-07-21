import { CoreIpcServerRunner } from '@test-helpers';
import { ipc, Message } from '@core';
import { CancellationToken, TimeSpan, TimeoutError, SocketStream } from '@foundation';

describe(`surface`, () => {
    describe(`end-to-end`, () => {
        @ipc.$service
        class IAlgebra {
            @ipc.$operation({ name: 'Ping' })
            public ping(): Promise<string> { throw null; }

            public MultiplySimple(x: number, y: number): Promise<number> { throw null; }

            public Multiply(x: number, y: number): Promise<number> { throw null; }

            public Sleep(milliseconds: number, message: Message, ct: CancellationToken): Promise<boolean> { throw null; }

            public Timeout(): Promise<boolean> { throw null; }
        }

        @ipc.$service
        class ICalculus {
            public Ping(): Promise<string> { throw null; }
        }

        class IArithmetics {
            public async Sum(x: number, y: number): Promise<number> {
                return x + y;
            }
        }

        const pipeName = 'some-pipe-name';

        ipc.config(pipeName, builder => builder
            .allowImpersonation()
            .setRequestTimeout(TimeSpan.fromHours(10)));

        ipc.callback.set(IArithmetics, pipeName, new IArithmetics());

        context(`the timeout`, () => {
            it(`should work`, async () => {
                const proxy = ipc.proxy.get(pipeName, IAlgebra);

                await CoreIpcServerRunner.host(pipeName, async () => {
                    await proxy.Sleep(
                        1000,
                        new Message(undefined, TimeSpan.fromMilliseconds(1)),
                        CancellationToken.none,
                    )
                        .should.eventually.be.rejectedWith(TimeoutError);
                });
            });

            it(`should unwrap a remote System.TimeoutException into TimeoutError`, async () => {
                const proxy = ipc.proxy.get(pipeName, IAlgebra);

                await CoreIpcServerRunner.host(pipeName, async () => {
                    await proxy.Timeout()
                        .should.eventually.be.rejectedWith(TimeoutError);
                });
            });
        });

        context(`multiple endpoints`, () => {
            it(`should work`, async () => {
                const proxy1 = ipc.proxy.get(pipeName, IAlgebra);
                const proxy2 = ipc.proxy.get(pipeName, ICalculus);

                await CoreIpcServerRunner.host(pipeName, async () => {
                    const promise1 = proxy1.ping();
                    const promise2 = proxy2.Ping();

                    await Promise.all([
                        promise1.should.eventually.be.fulfilled.and.be.eq('Pong'),
                        promise2.should.eventually.be.fulfilled.and.be.eq('Pong'),
                    ]);
                });
            }).timeout(10 * 1000);
        });

        it(`should work`, async () => {
            const algebra = ipc.proxy.get(pipeName, IAlgebra);

            await CoreIpcServerRunner.host(pipeName, async () => {
                await algebra.ping()
                    .should.eventually.be.fulfilled
                    .and.be.eq('Pong');

                await algebra.MultiplySimple(2, 3)
                    .should.eventually.be.fulfilled
                    .and.be.eq(6);

                await algebra.Multiply(2, 3)
                    .should.eventually.be.fulfilled
                    .and.be.eq(6);

            });
        }).timeout(10 * 1000);

    });
});
