import { Contract } from './contract';
import { Bootstrapper } from './bootstrapper';
import { IpcClient, Message, PromisePal, TimeSpan, CancellationToken, PromiseCompletionSource, PipeClientStream } from '@uipath/ipc';

// @ts-ignore
import * as child_process from 'child_process';
import * as readline from 'readline';

const readLine = readline.createInterface(process.stdin, process.stdout);

export class Program {

    private static get userDomain(): string { return (process.env.userDomain || '').toLowerCase(); }
    private static get userName(): string { return (process.env.userName || '').toLowerCase(); }

    public static async main(args: string[]): Promise<void> {
        // console.log(`process.cwd() === ${process.cwd()}`);
        // return;

        const INDENT = '                     ';
        const dir = `E:\\develop\\Studio\\Output\\bin\\Debug`;
        const path = `E:\\develop\\Studio\\Output\\bin\\Debug\\UiPath.Service.UserHost.exe`;

        class AgentEventsImpl extends Contract.IAgentEvents {
            // tslint:disable-next-line: no-shadowed-variable
            public async OnJobStatusUpdated(args: Contract.JobStatusChangedEventArgs): Promise<void> {
                console.debug(`${INDENT} ------> OnJobStatusUpdated with args:\r\n`, args);
                console.debug();
            }
            // tslint:disable-next-line: no-shadowed-variable
            public async OnJobCompleted(args: Contract.JobCompletedEventArgs): Promise<void> {
                console.debug(`${INDENT} ------> OnJobCompleted with args:\r\n`, args);
                console.debug();
            }
            // tslint:disable-next-line: no-shadowed-variable
            public async OnOrchestratorStatusChanged(args: Contract.OrchestratorStatusChangedEventArgs): Promise<void> {
                console.debug(`${INDENT} ------> OnOrchestratorStatusChanged with args:\r\n`, args);
                console.debug();
            }
            public async OnLogInSessionExpired(message: Message<void>): Promise<void> {
                console.debug(`${INDENT} ------> OnLogInSessionExpired with message:\r\n`, message);
                console.debug();
            }
        }

        const pipeName = `RobotEndpoint_${Program.userDomain}\\${Program.userName}`;
        const events = new AgentEventsImpl();
        const client = new IpcClient(
            pipeName,
            Contract.IAgentOperations,
            config => {
                config.callbackService = events;
                config.defaultCallTimeoutSeconds = 40;

                config.setConnectionFactory(async (connect, cancellationToken) => {
                    console.debug(`${INDENT} BEFORE CONNECT`);
                    console.debug(`${INDENT} SPAWNING ${path}`);
                    child_process.spawn(path, {
                        cwd: dir,
                        detached: true
                    }).unref();

                    while (true) {
                        cancellationToken.throwIfCancellationRequested();

                        try {
                            console.debug(`\r\n${INDENT} TRYING TO OPEN A NAMED-PIPE-STREAM`);
                            const result = await connect();
                            // await PromisePal.delay(TimeSpan.fromSeconds(1));
                            console.debug(`${INDENT} SUCCESS`);
                            return result;
                        } catch (error) {
                            const errorText = error instanceof Error
                                ? error.message
                                : `${error}`;
                            console.debug(`${INDENT} FAILED TO CONNECT: `, errorText);
                        }

                        const pause = TimeSpan.fromMilliseconds(300);
                        console.debug(`${INDENT} WAITING ${pause}`);
                        await PromisePal.delay(pause);
                    }
                });
                config.setBeforeCall(async (methodName: string, newConnection: boolean, ct: CancellationToken) => {
                    console.debug(`\r\n${INDENT} BEFORE CALL (methodName: `, methodName, `, newConnection: `, newConnection, `)`);
                    if (newConnection) {
                        console.debug(`\r\n${INDENT} RUNNING await client.proxy.SubscribeToEvents(new Message<void>())`);
                        try {
                            await client.proxy.SubscribeToEvents(new Message<void>());
                            console.debug(`\r\n${INDENT} SUCCESS!`);
                        } catch (error) {
                            console.debug(`\r\n${INDENT} ERROR!`, error);
                            throw error;
                        }
                    }
                });
            }
        );

        try {
            const action: () => Promise<void> = async () => {
                const res = await client.proxy.GetAvailableProcesses(
                    new Contract.GetProcessesParameters(true)
                );
                console.log(``);
                console.log(`Processes:`);
                console.log(`================`);
                let i = 0;
                for (const x of res) {
                    console.debug(`[${i++}] === `, x);
                    console.debug();
                }
                console.log(`================`);
                console.log(``);
            };
            await action();

            const pcs = new PromiseCompletionSource<void>();
            readLine.question('Press ENTER to call GetProcessParameters again.', _ => pcs.setResult(undefined));
            await pcs.promise;

            await action();

            await PromisePal.delay(TimeSpan.fromMinutes(10));
        } finally {
            await client.closeAsync();
        }
    }

}

Bootstrapper.start(async args => {
    await Program.main(args);
    readLine.close();
});
