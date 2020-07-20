import { CoreIpcServerRunner, expect } from '@test-helpers';
import { ipc } from '../../../../src/core/ipc';
import { TimeSpan } from '../../../../src/foundation/threading/time';

describe(`surface`, () => {

    describe(`end-to-end`, () => {
        it(`should work`, async () => {
            @ipc.$service
            class IAlgebra {
                @ipc.$operation({ name: 'Ping' })
                public ping(): Promise<string> { throw null; }

                public MultiplySimple(x: number, y: number): Promise<number> { throw null; }

                public Multiply(x: number, y: number): Promise<number> { throw null; }
            }

            class IArithmetics {
                public async Sum(x: number, y: number): Promise<number> {
                    return x + y;
                }
            }

            const pipeName = 'some-pipe-name';

            ipc.config.write(pipeName, builder => builder
                .allowImpersonation()
                .setRequestTimeout(TimeSpan.fromSeconds(2)));

            ipc.callback.set(IArithmetics, pipeName, new IArithmetics());

            const algebra = ipc.proxy.get(pipeName, IAlgebra);

            await CoreIpcServerRunner.host(pipeName, async () => {
                console.log('CoreIpc Server is running...');

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
