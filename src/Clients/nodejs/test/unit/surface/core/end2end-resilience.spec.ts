import * as path from 'path';
import * as fs from 'fs';
import { spawn, ChildProcessWithoutNullStreams } from 'child_process';
import { performance } from 'perf_hooks';

import { v4 as newGuid } from 'uuid';

import { ipc } from '@core';
import { TimeSpan, TimeoutError } from '@foundation';

import { expect } from '@test-helpers';

describe(`surface`, () => {
    context(`end-to-end-resilience`, () => {
        @ipc.$service
        class IBrittleService {
            @ipc.$operation
            public Sum(x: number, y: number, delay: TimeSpan, crashBeforeUtc: Date | null): Promise<number> { throw null; }

            @ipc.$operation
            public Kill(): Promise<void> { throw null; }
        }

        const pathServer = path.join(
            process.cwd(),
            'dotnet',
            'UiPath.CoreIpc.NodeInterop',
            'bin',
            'Debug',
            'netcoreapp3.1',
            'UiPath.CoreIpc.NodeInterop.exe',
        );

        it(`should eventually connect to delayed powered on servers`, async () => {
            const pipeName = newGuid();
            const mutualExclusionId = newGuid();

            let lastProcess: ChildProcessWithoutNullStreams | undefined;
            ipc.config(pipeName, builder => builder
                .setRequestTimeout(TimeSpan.fromSeconds(20))
                .setConnectHelper(async context => {
                    lastProcess = spawn(pathServer, [
                        '--pipe', `${pipeName}`,
                        '--delay', '1',
                        '--mutex', mutualExclusionId,
                    ]);

                    await context.tryConnect();
                }));

            const brittleService = ipc.proxy.get(pipeName, IBrittleService);

            let actual: number;
            const start = performance.now();
            const msAlloted = TimeSpan.fromSeconds(10).totalMilliseconds;

            while (true) {
                try {
                    actual = await brittleService.Sum(10, 20, TimeSpan.zero, null);
                    break;
                } catch (error) {
                    // console.error(error);
                }

                if (performance.now() - start > msAlloted) {
                    throw new TimeoutError({ reportedByServer: false });
                }
            }

            expect(actual).to.be.eq(30);

            try {
                lastProcess?.kill();
            } catch (error) {
                console.error(error);
            }
        }).timeout(30 * 1000);

        it(`should remain logically connected to intermittently failing server`, async () => {
            const pipeName = newGuid();
            const mutualExclusionId = newGuid();

            let lastProcess: ChildProcessWithoutNullStreams | undefined;

            ipc.config(pipeName, builder => builder
                .setRequestTimeout(TimeSpan.fromSeconds(20))
                .setConnectHelper(async context => {
                    if (!IOHelpers.pipeExists(pipeName)) {
                        lastProcess = spawn(pathServer, [
                            '--pipe', `${pipeName}`,
                            '--mutex', mutualExclusionId,
                        ]);
                    }
                    await context.tryConnect();
                }));

            const brittleService = ipc.proxy.get(pipeName, IBrittleService);

            const utcNow = new Date();
            const utcNowPlus3seconds = new Date();
            utcNowPlus3seconds.setTime(utcNow.getTime() + 3 * 1000);

            let actual: number;
            const start = performance.now();
            const msAlloted = TimeSpan.fromSeconds(30).totalMilliseconds;

            while (true) {
                try {
                    actual = await brittleService.Sum(10, 20, TimeSpan.zero, utcNowPlus3seconds);
                    break;
                } catch (error) {
                    // console.error(error);
                }

                if (performance.now() - start > msAlloted) {
                    throw new TimeoutError({ reportedByServer: false });
                }
            }

            expect(actual).to.be.eq(30);
            try {
                lastProcess?.kill();
            } catch (error) {
                console.error(error);
            }
        }).timeout(50 * 1000);
    });
});

class IOHelpers {
    public static pipeExists(pipeName: string): boolean {
        const fullPipeName = `\\\\.\\pipe\\${pipeName}`;
        const result = fs.existsSync(fullPipeName);

        return result;
    }
}
