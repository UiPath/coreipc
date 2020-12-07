import { v4 as uuid } from 'uuid';
import * as path from 'path';
import { spawn, spawnSync } from 'child_process';

import { ipc, TimeSpan, ConnectContext } from '@uipath/core-ipc';
import { IOHelpers } from './IOHelpers';

@ipc.$service
class IArithmetics {
    @ipc.$operation
    public Sum(x: number, y: number, delay: TimeSpan, failBeforeUtc: Date): Promise<number> { throw null; }
}

async function main() {
    const pathServer = path.join(
        process.cwd(),
        '..',
        'UiPath.CoreIpc.BrittleServer',
        'bin',
        'Debug',
        'net5.0',
        'UiPath.CoreIpc.BrittleServer.exe',
    );
    const pipeName = uuid();

    ipc.config(
        pipeName,
        builder => builder
            .setRequestTimeout(TimeSpan.fromSeconds(20))
            .setConnectHelper(async (context: ConnectContext) => {
                if (!IOHelpers.pipeExists(pipeName)) {
                    console.warn('Pipe not found. Starting server...');
                    const proc = spawn(pathServer, [pipeName], {
                        stdio: 'pipe',
                        shell: true,
                    });
                    proc.stdout.observeLines(console.log);
                    proc.stderr.observeLines(console.error);
                }
                await context.tryConnect();
            }));

    const arithmetics = ipc.proxy.get(pipeName, IArithmetics);
    const utcNow = new Date();
    const utcNowPlus10s = new Date();
    utcNowPlus10s.setTime(utcNow.getTime() + 10 * 1000);

    let result: number;

    while (true) {
        try {
            result = await arithmetics.Sum(10, 20, TimeSpan.fromSeconds(1), utcNowPlus10s);
            break;
        } catch (error) {
            console.error('The time is ', new Date(), error);
        }
    }

    console.log(`result === ${result}`);
}

main();
