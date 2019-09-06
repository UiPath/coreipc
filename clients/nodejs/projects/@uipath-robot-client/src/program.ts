import { Contract } from './contract';
import { Bootstrapper } from './bootstrapper';
import { IpcClient, Message, PromisePal, PromiseCompletionSource, CancellationToken, TimeSpan, Timeout } from '@uipath/ipc';

export class Program {

    public static async main(args: string[]): Promise<void> {

        class AgentEventsImpl implements Contract.IAgentEvents {

            public async OnJobStatusUpdated(args: any): Promise<boolean> {
                console.debug(`   >>>> OnJobStatusUpdated got some args: `, args);
                return true;
            }
            public async OnJobCompleted(args: Contract.JobCompletedEventArgs): Promise<boolean> {
                console.debug(`   >>>> OnJobCompleted got some args: `, args);
                return true;
            }

        }

        const pipeName = 'RobotEndpoint_uipath\\eduard.dumitru';
        const events = new AgentEventsImpl();
        const client = new IpcClient(pipeName, Contract.IAgentOperations, config => config.callbackService = events);

        try {
            await client.proxy.SubscribeToEvents(new Message<void>());

            const res = await client.proxy.GetAvailableProcesses(
                new Contract.GetProcessesParameters(true)
            );
            let i = 0;
            for (const x of res) {
                console.debug(`[${i++}] === `, x);
            }

            await PromisePal.delay(TimeSpan.fromMinutes(10));
        } finally {
            await client.closeAsync();
        }
    }

}

Bootstrapper.start(Program.main);
