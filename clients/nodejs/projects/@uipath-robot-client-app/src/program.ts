import {
    RobotProxyConstructor, StartJobParameters
} from '@uipath/robot-client';
import { PromiseHook } from './promise-hook';

async function main() {
    // interface SetTimeoutData {
    //     readonly id: symbol;
    //     readonly stack: string;
    //     readonly timeoutId: NodeJS.Timeout;
    // }
    // const remainingTimeouts = new Array<SetTimeoutData>();

    // const oldSetTimeout = setTimeout;
    // global['setTimeout'] = function (callback: (...args: any[]) => void, ms: number, ...args: any[]): NodeJS.Timeout {
    //     const desc = `setTimeout: ms == ${ms}, callback == ${callback}`;
    //     const id = Symbol(desc);

    //     function callbackHook(..._args: any[]): void {
    //         const index = remainingTimeouts.findIndex(x => x.id === id);
    //         if (index >= 0) { remainingTimeouts.splice(index, 1); }

    //         callback(...args);
    //     }

    //     let stack: string = null as any;
    //     try {
    //         throw new Error();
    //     } catch (error) {
    //         stack = error.stack;
    //     }

    //     const timeoutId = oldSetTimeout(callbackHook, ms, ...args);

    //     remainingTimeouts.push({ id, stack, timeoutId });

    //     return timeoutId;
    // };

    // const oldClearTimeout = clearTimeout;
    // global['clearTimeout'] = function (timeoutId: NodeJS.Timeout): void {
    //     const index = remainingTimeouts.findIndex(x => x.timeoutId === timeoutId);
    //     if (index >= 0) {
    //         remainingTimeouts.splice(index, 1);
    //     }
    // };

    // const oldSetImmediate = setImmediate;
    // global['setImmediate'] = function (callback: (...args: any[]) => void, ...args: any[]): NodeJS.Immediate {
    //     console.log(`setImmediate: callback == `, callback);
    //     return oldSetImmediate(callback, ...args);
    // };

    // PromiseHook.install();

    // tslint:disable-next-line: variable-name
    const Dune2000__Key = 'e0880c07-a0a2-488b-9bc6-84a4040bd012';

    console.log(`Starting...`);
    const client = new RobotProxyConstructor();

    client.RefreshStatus({ ForceProcessListUpdate: false });
    await client.StartJob(new StartJobParameters(Dune2000__Key));
    console.log(`Closing...`);
    await client.CloseAsync();
    console.log(`Closed.`);

    // console.log('');
    // console.log('');
    // console.log('Remaining promises:');
    // console.log('==================');
    // for (const hook of PromiseHook.remaining()) {
    //     console.log(hook);
    //     console.log();
    // }

    // console.log('');
    // console.log('');
    // console.log('Remaining timeouts:');
    // console.log('==================');
    // for (const data of remainingTimeouts) {
    //     console.log(data);
    //     console.log();
    // }
}

main();
