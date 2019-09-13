import { RobotConfig, RobotProxyConstructor } from '@uipath/robot-client';
import { Trace } from '@uipath/ipc';

async function main() {
    const proxy = new RobotProxyConstructor();

    console.log(`program.ts: env is: `, RobotConfig.data);
    Trace.addListener(x => console.log(x));

    console.log('program.ts: Starting...');
    try {
        await proxy.RefreshStatus({ ForceProcessListUpdate: true });

        console.log('program.ts: Success!');
    } catch (error) {
        console.error(`program.ts: caught error: `, error);
        throw error;
    }
}

main().then(
    _ => { console.log(`program.ts: main ended successfully`); },
    err => { console.error(`program.ts: main ended with error`, err); }
);
