import { Terminal } from './terminal';
import { Bootstrapper } from './bootstrapper';
import * as fs from 'fs';

import { Trace } from '@uipath/ipc';
import {
    RobotProxyConstructor,
    IRobotAgentProxy,

    RobotConfig,

    RobotStatus,
    LocalProcessInformation,
    StartJobParameters,
    StopJobParameters,
    InstallProcessParameters,
    PauseJobParameters,
    ResumeJobParameters
} from '@uipath/robot-client';
import { ProcessInstallationState } from '@uipath/robot-client/dist/upstream-contract';

export class Program {
    private static readonly terminal = new Terminal(settings => {
        settings.title = '@uipath/robot-client Tester App';
    });

    private static exitRequested = false;
    private static command: string | null = null;

    private static proxy: IRobotAgentProxy = null as any;

    private static readonly _trace = Trace.category('app');

    public static async main(args: string[]): Promise<void> {
        const colorReset = '\x1b[0m';
        const colorRedFg = '\x1b[31m';
        const colorGreenFg = '\x1b[32m';
        const colorYellowFg = '\x1b[33m';
        const colorWhiteFg = '\x1b[37m';

        Program.terminal.initialize('');
        Trace.addListener((errorOrText, category) => {
            const prefix = `${colorYellowFg}[${(category || 'uncategorized').padStart(20)}]${colorReset}`;
            const message: string = (errorOrText instanceof Error)
                ? `${prefix}: ${colorRedFg}--! ${errorOrText}${colorReset}`
                : `${prefix}: ${colorGreenFg}--> ${errorOrText}${colorReset}`;

            Program.terminal.writeLine(message);
            // console.log(message);
        });

        Program.proxy = new RobotProxyConstructor();
        try {

            Program.proxy.JobCompleted.subscribe(_args => {
                Program.terminal.writeLine(`      [{green-fg}event{/green-fg} JobCompleted] (DisplayName === ${_args.Job.DisplayName})`);
            });

            Program.proxy.JobStatusChanged.subscribe(_args => {
                Program.terminal.writeLine(`      [{green-fg}event{/green-fg} JobStatusChanged] (DisplayName === ${_args.Job.DisplayName}, StatusText === ${_args.StatusText})`);
            });

            Program.proxy.RobotStatusChanged.subscribe(_args => {
                function robotStatusToString(status: RobotStatus): string {
                    switch (status) {
                        case RobotStatus.Connected: return 'Connected';
                        case RobotStatus.Connecting: return 'Connecting';
                        case RobotStatus.ConnectionFailed: return 'ConnectionFailed';
                        case RobotStatus.LogInFailed: return 'LogInFailed';
                        case RobotStatus.LoggingIn: return 'LoggingIn';
                        case RobotStatus.Offline: return 'Offline';
                        case RobotStatus.ServiceUnavailable: return 'ServiceUnavailable';
                        default: return `Unexpected RobotStatus value (${status})`;
                    }
                }
                Program.terminal.writeLine(`      [{green-fg}event{/green-fg} RobotStatusChanged] (RobotStatus === ${robotStatusToString(_args.Status)}, LogInError === ${_args.LogInError})`);
            });

            Program.proxy.ProcessListUpdated.subscribe(_args => {
                const text = `*********  ProcessListUpdated: ${JSON.stringify(_args)}`;
                // console.log(text);
                Program._trace.log(text);

                // Program.terminal.writeLine(text);

                Program.terminal.writeLine(`      [{green-fg}event{/green-fg} ProcessListUpdated] (args.Processes === ${_args.Processes})`);
                // Program.terminal.writeLine(`      [{green-fg}event{/green-fg} ProcessListUpdated] (args.Processes.length === ${_args.Processes.length})`);

                function processToString(process: LocalProcessInformation): string {
                    Program._trace.log(`LocalProcessInformation === ${JSON.stringify(process)}`);

                    return `name: {yellow-fg}${process.Process.Name}{/yellow-fg}
Version: {yellow-fg}${process.Process.Version}{/yellow-fg}
Key: {yellow-fg}${process.Process.Key}{/yellow-fg}
FolderName: {yellow-fg}${process.Process.FolderName}{/yellow-fg}
FolderPath: {yellow-fg}${process.Process.FolderPath}{/yellow-fg}
InstallationState: {yellow-fg}${ProcessInstallationState[process.InstallationState]}{/yellow-fg}
`;
                }

                let annex = '{green-fg}Processes:{/green-fg}\r\n=====================\r\n\r\n';
                for (const process of _args.Processes) {
                    annex += processToString(process);
                    annex += '\r\n\r\n';
                }

                Program.terminal.annex = annex;
            });

            Program.proxy.RefreshStatus({
                ForceProcessListUpdate: true
            });

            Program.help();
            while (!Program.exitRequested) {
                Program.command = await Program.terminal.readLine();
                const parts = [Program.command];

                switch (parts.length) {
                    case 1:
                        switch (parts[0].toLowerCase()) {
                            case 'exit':
                                Program.terminal.dispose();
                                Program.exitRequested = true;
                                process.exit();
                                break;
                            case 'help':
                                Program.help();
                                break;
                            case 'env':
                                {
                                    for (const key in RobotConfig.data) {
                                        if (typeof key === 'string') {
                                            // tslint:disable-next-line: max-line-length
                                            Program.terminal.writeLine(`     env.{green-fg}${key}{/green-fg} === {yellow-fg}${JSON.stringify((RobotConfig.data as any)[key])}{/yellow-fg}`);
                                        }
                                    }
                                    Program.terminal.writeLine();
                                }
                                break;
                            case 'refresh':
                                {
                                    Program.terminal.write('Refreshing......');
                                    try {
                                        Program.proxy.RefreshStatus({ ForceProcessListUpdate: true });
                                        Program.terminal.writeLine('DONE!');
                                    } catch (error) {
                                        Trace.log(error);
                                    }
                                }
                                break;
                            case 'refreshnoforce':
                                {
                                    Program.terminal.write('Refreshing......');
                                    try {
                                        Program.proxy.RefreshStatus({ ForceProcessListUpdate: false });
                                        Program.terminal.writeLine('DONE!');
                                    } catch (error) {
                                        Trace.log(error);
                                    }
                                }
                                break;
                            case 'settings':
                                {
                                    await Program.proxy.OpenOrchestratorSettings();
                                }
                                break;
                            case 'start':
                                {
                                    Program.terminal.writeLine(`  ProcessKey (empty string to cancel):`);
                                    const processKey = await Program.terminal.readLine();
                                    if (processKey) {
                                        try {
                                            const jobData = await Program.proxy.StartJob(new StartJobParameters(processKey));
                                            Program.terminal.writeLine(`   DisplayName: {yellow-fg}"${jobData.DisplayName}"{/yellow-fg}`);
                                            Program.terminal.writeLine(`   Identifier: {yellow-fg}"${jobData.Identifier}"{/yellow-fg}`);
                                            Program.terminal.writeLine(`   ProcessData: {yellow-fg}"${JSON.stringify(jobData.Process)}"{/yellow-fg}`);
                                        } catch (error) {
                                            Trace.log(error);
                                        }
                                    }
                                }
                                break;
                            case 'stop':
                                {
                                    Program.terminal.writeLine(`  JobIdentifier (empty string to cancel):`);
                                    const jobIdentifier = await Program.terminal.readLine();
                                    if (jobIdentifier) {
                                        try {
                                            await Program.proxy.StopJob(new StopJobParameters(jobIdentifier));
                                            Program.terminal.writeLine('Finished without error.');
                                        } catch (error) {
                                            Trace.log(error);
                                        }
                                    }
                                }
                                break;
                            case 'install':
                                {
                                    Program.terminal.writeLine(`  ProcessKey (empty string to cancel):`);
                                    const processKey = await Program.terminal.readLine();
                                    if (processKey) {
                                        try {
                                            await Program.proxy.InstallProcess(new InstallProcessParameters(processKey));
                                            Program.terminal.writeLine('Finished without error.');
                                        } catch (error) {
                                            Trace.log(error);
                                        }
                                    }
                                }
                                break;
                            case 'pause':
                                {
                                    Program.terminal.writeLine(`  JobIdentifier (empty string to cancel):`);
                                    const jobIdentifier = await Program.terminal.readLine();
                                    if (jobIdentifier) {
                                        try {
                                            await Program.proxy.PauseJob(new PauseJobParameters(jobIdentifier));
                                            Program.terminal.writeLine('Finished without error.');
                                        } catch (error) {
                                            Trace.log(error);
                                        }
                                    }
                                }
                                break;
                            case 'resume':
                                {
                                    Program.terminal.writeLine(`  JobIdentifier (empty string to cancel):`);
                                    const jobIdentifier = await Program.terminal.readLine();
                                    if (jobIdentifier) {
                                        try {
                                            await Program.proxy.ResumeJob(new ResumeJobParameters(jobIdentifier));
                                            Program.terminal.writeLine('Finished without error.');
                                        } catch (error) {
                                            Trace.log(error);
                                        }
                                    }
                                }
                                break;
                        }
                        break;
                }
            }

        } finally {
            await Program.proxy.CloseAsync();
        }
    }

    public static help(): void {
        Program.terminal.writeLine();
        Program.terminal.writeLine('{yellow-fg}Commands:{/yellow-fg}');
        Program.terminal.writeLine('---------------------------');
        Program.terminal.writeLine('  {yellow-fg}help{/yellow-fg}       Displays this manual.');
        Program.terminal.writeLine('  {yellow-fg}env{/yellow-fg}        Displays the current config.');
        Program.terminal.writeLine(`  {yellow-fg}refresh{/yellow-fg}    Refreshes the status with force === true.`);
        Program.terminal.writeLine(`  {yellow-fg}settings{/yellow-fg}   Opens Orchestrator Settings in the old tray app.`);
        Program.terminal.writeLine(`  {yellow-fg}start{/yellow-fg}      Starts a job.`);
        Program.terminal.writeLine('  {yellow-fg}stop{/yellow-fg}       Stops a job.');
        Program.terminal.writeLine('  {yellow-fg}install{/yellow-fg}    Installs a process.');
        Program.terminal.writeLine('  {yellow-fg}pause{/yellow-fg}      Pauses a job.');
        Program.terminal.writeLine('  {yellow-fg}resume{/yellow-fg}     Resumes a job.');
        Program.terminal.writeLine('  {yellow-fg}exit{/yellow-fg}       Gracefully closes the client app (disconnecting if needed).');
        Program.terminal.writeLine('---------------------------');
        Program.terminal.writeLine();
        Program.terminal.writeLine();
    }

}

Bootstrapper.start(Program.main);
