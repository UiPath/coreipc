import {
    RobotProxyConstructor, StartJobParameters
} from '@uipath/robot-client';

async function main() {
    // tslint:disable-next-line: variable-name
    const Dune2000__Key = 'e0880c07-a0a2-488b-9bc6-84a4040bd012';

    console.log(`Starting...`);
    const client = new RobotProxyConstructor();

    client.RefreshStatus({ ForceProcessListUpdate: false });
    await client.StartJob(new StartJobParameters(Dune2000__Key));
    console.log(`Closing...`);
    await client.CloseAsync();
    console.log(`Closed.`);
}

main();
