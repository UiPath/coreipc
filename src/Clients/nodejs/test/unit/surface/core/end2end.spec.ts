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
            }

            const pipeName = 'some-pipe-name';

            ipc.config.write(pipeName, builder => builder
                .allowImpersonation()
                .setRequestTimeout(TimeSpan.fromSeconds(2)));

            const algebra = ipc.proxy.get(pipeName, IAlgebra);

            await CoreIpcServerRunner.host(pipeName, async () => {
                console.log('CoreIpc Server is running...');
                const actual = await algebra.ping();
                expect(actual).to.be.eq('Pong');
            });
        }).timeout(10 * 1000);

    });
});
