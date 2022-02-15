import * as util from 'util';
import { CoreIpcServerRunner, waitAllAndPrintAnyErrors as waitAllAndPrintAnyErrors } from '@test-helpers';
import { CancellationToken, TimeSpan, TimeoutError, SocketStream, Trace } from '@foundation';
import { ipc, Message } from '@core';
import { performance } from 'perf_hooks';

describe(`surface`, () => {
    describe(`end-to-end`, () => {
        const BIG = TimeSpan.fromHours(1);

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

        @ipc.$service
        class IDto {
            constructor(args?: {
                refresh: boolean,
                refreshTimeout: number,
                arguments: string | null,
                dtoMessage: Message
            }) {
                this.Refresh = args?.refresh;
                this.RefreshTimeout = args?.refreshTimeout;
                this.Arguments = args?.arguments;
                this.DtoMessage = args?.dtoMessage;
            }
        
            public Refresh?: boolean;
            public RefreshTimeout?: number;
            public Arguments?: string | null;
            public DtoMessage?: Message;

            public async ReturnDto(myDto: IDto): Promise<IDto> { return myDto; }
        }

        const pipeName = 'b87d1d64-0d84-4678-81a0-594028662385';
        ipc.config(pipeName, builder => builder.setRequestTimeout(BIG));
        ipc.callback.set(IArithmetics, pipeName, new IArithmetics());
        ipc.callback.set(IDto, pipeName, new IDto());

        context('default params', () => {
            it('should work for default numeric params', async () => {
                const algebra = ipc.proxy.get(pipeName, IAlgebra);
                await CoreIpcServerRunner.host(pipeName, async () => {
                    await algebra.MultiplySimple(0, 0)
                        .should.eventually.be.fulfilled
                        .and.be.eq(0);
    
                    await algebra.Multiply(0, 0)
                        .should.eventually.be.fulfilled
                        .and.be.eq(0);
                    });
                });

            it('should work for default complex params', async () => {
                const algebra = ipc.proxy.get(pipeName, IAlgebra);
                await CoreIpcServerRunner.host(pipeName, async () => {
                    await algebra.Sleep(0, new Message({ requestTimeout: TimeSpan.fromMilliseconds(0) }), CancellationToken.none)
                    .should.eventually.be.rejectedWith(TimeoutError)
                    });
                });

            it('should work for default dto params', async () => {
                const dto = ipc.proxy.get(pipeName, IDto);
                await CoreIpcServerRunner.host(pipeName, async () => {
                    dto.ReturnDto(new IDto ({refresh: false, refreshTimeout: 0, arguments: null, dtoMessage: new Message({ requestTimeout: TimeSpan.fromMilliseconds(0) })}))
                    .should.eventually.be.fulfilled.and.be.eq(new IDto ({refresh: false, refreshTimeout: 0, arguments: null, dtoMessage: new Message({ requestTimeout: TimeSpan.fromMilliseconds(0) })}));
                    });
                });           
        });
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
                    ipc.config(pipeName, builder => builder.setRequestTimeout(BIG));
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
                        .should.eventually.be.rejectedWith(TimeoutError)
                        .which.has.property('reportedByServer', true);
                });
            });

            it(`should work with infinite timeout`, async () => {
                const proxy = ipc.proxy.get(pipeName, IAlgebra);

                await CoreIpcServerRunner.host(pipeName, async () => {
                    await proxy.Timeout()
                        .should.eventually.be.rejectedWith(TimeoutError)
                        .which.has.property('reportedByServer', true);
                });
            });
        });

        context(`concurrent calls`, () => {
            it(`should work`, async () => {
                const proxy = ipc.proxy.get(pipeName, IAlgebra);
                const concurrencyLevel = 100;

                let assertError: Error | undefined;
                await CoreIpcServerRunner.host(pipeName, async () => {
                    const subscription = Trace.addListener(unit => console.log(util.inspect(unit, {
                        colors: true,
                        depth: null,
                        maxArrayLength: null,
                    })));
                    try {
                        ipc.config(pipeName, builder => builder.setRequestTimeout(BIG));

                        const infos = Array.from(Array(concurrencyLevel).keys())
                            .map(input => ({
                                input,
                                output: proxy.Echo(input),
                            }));

                        await waitAllAndPrintAnyErrors(...infos.map(info => info.output));

                        try {
                            for (const info of infos) {
                                await info.output.should.eventually.be.fulfilled.and.be.eq(info.input);
                            }
                        } catch (err) {
                            assertError = err;
                        }
                    } finally {
                        subscription.dispose();
                    }
                });
                if (assertError) { throw assertError; }
            }).timeout(30 * 1000);
        });

        context(`multiple endpoints`, () => {
            it(`should work`, async () => {
                const proxy1 = ipc.proxy.get(pipeName, IAlgebra);
                const proxy2 = ipc.proxy.get(pipeName, ICalculus);

                let assertError: Error | undefined;
                await CoreIpcServerRunner.host(pipeName, async () => {
                    ipc.config(pipeName, builder => builder.setRequestTimeout(BIG));

                    const promise1 = proxy1.ping();
                    const promise2 = proxy2.Ping();

                    await waitAllAndPrintAnyErrors(promise1, promise2);
                    try {
                        await Promise.all([
                            promise1.should.eventually.be.fulfilled.and.be.eq('Pong'),
                            promise2.should.eventually.be.fulfilled.and.be.eq('Pong'),
                        ]);
                    } catch (err) {
                        assertError = err;
                    }
                });
                if (assertError) { throw assertError; }
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
