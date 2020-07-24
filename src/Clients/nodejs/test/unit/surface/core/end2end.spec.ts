import * as util from 'util';
import { CoreIpcServerRunner } from '@test-helpers';
import { CancellationToken, TimeSpan, TimeoutError, SocketStream, Trace } from '@foundation';
import { ipc, Message } from '@core';
import { performance } from 'perf_hooks';

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
            public Echo(x: number): Promise<number> { throw null; }
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
        ipc.config(pipeName, builder => builder.setRequestTimeout(TimeSpan.fromHours(10)));
        ipc.callback.set(IArithmetics, pipeName, new IArithmetics());

        context(`the timeout`, () => {
            it(`should be configurable via config`, async () => {
                const proxy = ipc.proxy.get(pipeName, IAlgebra);
                ipc.config(pipeName, builder => builder.setRequestTimeout(TimeSpan.fromMilliseconds(1)));

                try {

                    await CoreIpcServerRunner.host(pipeName, async () => {
                        const start = performance.now();
                        await proxy.Sleep(10 * 1000, new Message(), CancellationToken.none)
                            .should.eventually.be.rejectedWith(TimeoutError);
                        const end = performance.now();
                        (end - start).should.be.lessThan(2000);
                    });

                } finally {
                    ipc.config(pipeName, builder => builder.setRequestTimeout(TimeSpan.fromHours(10)));
                }
            });

            it(`should be configurable via Message`, async () => {
                const proxy = ipc.proxy.get(pipeName, IAlgebra);

                await CoreIpcServerRunner.host(pipeName, async () => {
                    const start = performance.now();
                    await proxy.Sleep(
                        10 * 1000,
                        new Message({ requestTimeout: TimeSpan.fromMilliseconds(1) }),
                        CancellationToken.none,
                    )
                        .should.eventually.be.rejectedWith(TimeoutError);
                    const end = performance.now();
                    (end - start).should.be.lessThan(2000);
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

        context(`concurrent calls`, () => {
            it(`should work`, async () => {
                const proxy = ipc.proxy.get(pipeName, IAlgebra);
                const concurrencyLevel = 3;

                await CoreIpcServerRunner.host(pipeName, async () => {
                    const subscription = Trace.addListener(unit => console.log(util.inspect(unit, {
                        colors: true,
                        depth: null,
                        maxArrayLength: null,
                    })));
                    try {
                        const infos = Array.from(Array(concurrencyLevel).keys())
                            .map(input => ({
                                input,
                                output: proxy.Echo(input),
                            }));

                        for (const info of infos) {
                            await info.output.should.eventually.be.fulfilled.and.be.eq(info.input);
                        }
                    } finally {
                        subscription.dispose();
                    }
                });
            }).timeout(30 * 1000);
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
            }).timeout(30 * 1000);
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
