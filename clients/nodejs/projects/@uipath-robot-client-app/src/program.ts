import {
    RobotProxyConstructor, StartJobParameters
} from '@uipath/robot-client';
import { Trace } from '@uipath/ipc';

async function main() {
    const key = 'e0880c07-a0a2-488b-9bc6-84a4040bd012';

    Trace.addListener((errorOrText, category) => {
        console.log(`${category}:`, errorOrText);
    });

    console.log(`Starting...`);
    const client = new RobotProxyConstructor();

    client.RefreshStatus({ ForceProcessListUpdate: false });

    await Promise.delay(1000);

    console.log(`Closing...`);
    await client.CloseAsync();
    console.log(`Closed.`);
}

main();
