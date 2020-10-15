import { ipc } from '@core';
import { v4 as newGuid } from 'uuid';
import { CoreIpcServerRunner } from '@test-helpers';

if (process.platform !== 'win32') {
    const TMPDIR = 'TMPDIR';

    @ipc.$service
    class IEnvironmentVariableGetter {
        @ipc.$operation
        public Get(variable: string): Promise<string | null> { throw void 0; }
    }

    describe(`surface`, () => {
        describe(`pipe name matching on linux`, () => {
            it(`should match pipe names when $TMPDIR is not set`, async () => {
                await logic('', '');
            });

            it(`should match pipe names when $TMPDIR is set`, async () => {
                const newTmpDir = `${process.env['HOME']}/`;
                await logic(newTmpDir, newTmpDir);
            });

            async function logic(tmpDir: string, expectedDotNetResult: string | null): Promise<void> {
                const old = process.env[TMPDIR] || '';
                try {
                    process.env[TMPDIR] = tmpDir;
                    const pipeName = newGuid();

                    await CoreIpcServerRunner.host(pipeName, async () => {
                        const evg = ipc.proxy.get(pipeName, IEnvironmentVariableGetter);
                        await evg.Get(TMPDIR)
                            .should.eventually.be.fulfilled
                            .and.be.eq(expectedDotNetResult);
                    });
                } finally {
                    process.env[TMPDIR] = old;
                }
            }
        });
    });
}
