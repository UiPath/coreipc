import { RobotConfig, RobotProxyConstructor, RobotStatus } from '@uipath/robot-client';
import { Trace } from '@uipath/ipc';

async function main() {
    const youWantDebug = false; // change this!

    if (youWantDebug) {
        Trace.addListener(x => console.log(x));
    }

    function toStatusString(x: RobotStatus): string {
        switch (x) {
            case RobotStatus.Connected: return 'Connected';
            case RobotStatus.Connecting: return 'Connecting';
            case RobotStatus.ConnectionFailed: return 'ConnectionFailed';
            case RobotStatus.LogInFailed: return 'LogInFailed';
            case RobotStatus.LoggingIn: return 'LoggingIn';
            case RobotStatus.Offline: return 'ConOfflinenected';
            case RobotStatus.ServiceUnavailable: return 'ServiceUnavailable';
            default: return 'Unknown';
        }
    }

    const proxy = new RobotProxyConstructor();
    proxy.RobotStatusChanged.subscribe(x => {
        console.log(`$ RobotStatusChanged: ${x.Status} ${toStatusString(x.Status)}`);
    });

    console.log(`program.ts: env is: `, RobotConfig.data);

    console.log('program.ts: Starting...');
    proxy.RefreshStatus({ ForceProcessListUpdate: true });

    // program will not close gracefully (intended)
    // await some time or some event and call process.exit()
}

main().then(
    _ => { console.log(`program.ts: main ended successfully`); },
    err => { console.error(`program.ts: main ended with error`, err); }
);
