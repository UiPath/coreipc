import { CancellationToken, ipc, TimeSpan } from '@uipath/coreipc';
import { performance } from 'perf_hooks';
import { inspect } from 'util';
import * as contracts from './contracts';

async function main(): Promise<void> {

    console.log('Starting...');

    const pipeName = 'test';
    const start = performance.now();
    const pipeExists = await ipc.pipeExists('test');
    const stop = performance.now();
    console.log(`(await ipc.pipeExists('${pipeName}')) yields ${pipeExists} in ${stop - start} milliseconds`);

    if (pipeExists) {
        ipc.config(options => {
            options.setRequestTimeout(TimeSpan.fromSeconds(5));
        });

        const computingService = ipc.proxy.get('test', contracts.IComputingService);

        const a = new contracts.ComplexNumber(1, 2);
        const b = new contracts.ComplexNumber(3, 4);
        const c = await computingService.AddComplexNumber(a, b, CancellationToken.none);

        console.log('c === ', inspect(c));
    }
    process.exit();
}

const _ = main();
