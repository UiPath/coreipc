import { ipc } from '@core';
import { v4 as newGuid } from 'uuid';
import { CoreIpcServerRunner } from '@test-helpers';

describe(`surface`, () => {
    describe(`pipe-exists`, () => {
        it(`should return false for inexistent pipes`, async () => {

            await ipc.pipeExists('825137f4-3885-4547-ba3a-655a92104f0d')
                .should.eventually.be.fulfilled
                .and.be.false;

        });

        it(`should return true for existing pipes`, async () => {

            const pipeName = newGuid();
            await CoreIpcServerRunner.host(pipeName, async () => {

                await ipc.pipeExists(pipeName)
                    .should.eventually.be.fulfilled
                    .and.be.true;

            });

        });
    });
});
