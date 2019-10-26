import {
    RobotProxyConstructor, StartJobParameters
} from '@uipath/robot-client';

// tslint:disable-next-line: variable-name
const Dune2000__Key = 'e0880c07-a0a2-488b-9bc6-84a4040bd012';

const client = new RobotProxyConstructor();
client.RefreshStatus({ ForceProcessListUpdate: false });
client.StartJob(new StartJobParameters(Dune2000__Key)).then(() => {
    client.CloseAsync();
});
