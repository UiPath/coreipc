import { TerminalBase } from './terminal-base';
import { Bootstrapper } from './bootstrapper';

import { Trace } from '@uipath/ipc';
import {
    RobotProxyConstructor,
    RobotStatus,
    LocalProcessInformation,
    StartJobParameters,
    StopJobParameters
} from '@uipath/robot-client';

export class Program {
    private static readonly terminal = new TerminalBase(settings => {
        settings.title = '@uipath/robot-client Tester App';
    });
    private static exitRequested = false;
    private static command: string | null = null;

    public static async main(args: string[]): Promise<void> {
        Program.terminal.initialize('');

        Trace.addListener(errorOrText => {
            if (errorOrText instanceof Error) {
                Program.terminal.writeLine(`{red-fg}--! ${errorOrText}{/red-fg}`);
            } else {
                Program.terminal.writeLine(`{green-fg}--> ${errorOrText}{/green-fg}`);
            }
        });

        const proxy = new RobotProxyConstructor();

        proxy.JobCompleted.subscribe(_args => {
            Program.terminal.writeLine(`$$ JobCompleted (DisplayName === ${_args.Job.DisplayName})`);
        });

        proxy.JobStatusChanged.subscribe(_args => {
            Program.terminal.writeLine(`$$ JobStatusChanged (DisplayName === ${_args.Job.DisplayName}, StatusText === ${_args.StatusText})`);
        });

        proxy.RobotStatusChanged.subscribe(_args => {
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
            Program.terminal.writeLine(`$$ RobotStatusChanged (RobotStatus === ${robotStatusToString(_args.Status)}, LogInError === ${_args.LogInError})`);
        });

        proxy.ProcessListUpdated.subscribe(_args => {
            Program.terminal.writeLine(`$$ ProcessListUpdated (args.Processes.length === ${_args.Processes.length})`);

            function processToString(process: LocalProcessInformation): string {
                return `name: {yellow-fg}${process.Process.Name}{/yellow-fg}
version: {yellow-fg}${process.Process.Version}{/yellow-fg}
key: {yellow-fg}${process.Process.Key}{/yellow-fg}
folder: {yellow-fg}${process.Process.FolderName}{/yellow-fg}
installed: {yellow-fg}${process.Installed}{/yellow-fg}
autoInstall: {yellow-fg}${process.Settings.AutoInstall}{/yellow-fg}
autoStart: {yellow-fg}${process.Settings.AutoStart}{/yellow-fg}
`;
            }

            let annex = '{green-fg}Processes:{/green-fg}\r\n=====================\r\n\r\n';
            for (const process of _args.Processes) {
                annex += processToString(process);
                annex += '\r\n\r\n';
            }

            Program.terminal.annex = annex;
        });

        Trace.log(`Calling RefreshStatus.....`);
        await proxy.RefreshStatus({
            ForceProcessListUpdate: true
        });
        Trace.log(`                     .....DONE!`);

        // Program.help();
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
                        case 'start job':
                            {
                                Program.terminal.writeLine(`  ProcessKey (empty string to cancel):`);
                                const processKey = await Program.terminal.readLine();
                                if (processKey) {
                                    try {
                                        const jobData = await proxy.StartJob(new StartJobParameters(processKey));
                                        Program.terminal.writeLine(`   DisplayName: {yellow-fg}"${jobData.DisplayName}"{/yellow-fg}`);
                                        Program.terminal.writeLine(`   Identifier: {yellow-fg}"${jobData.Identifier}"{/yellow-fg}`);
                                        Program.terminal.writeLine(`   ProcessData: {yellow-fg}"${JSON.stringify(jobData.ProcessData)}"{/yellow-fg}`);
                                    } catch (error) {
                                        Trace.log(error);
                                    }
                                }
                            }
                            break;
                        case 'stop job':
                            {
                                Program.terminal.writeLine(`  JobIdentifier (empty string to cancel):`);
                                const jobIdentifier = await Program.terminal.readLine();
                                if (jobIdentifier) {
                                    try {
                                        await proxy.StopJob(new StopJobParameters(jobIdentifier));
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
    }

    public static help(): void {
        Program.terminal.writeLine();
        Program.terminal.writeLine('{yellow-fg}Commands:{/yellow-fg}');
        Program.terminal.writeLine('---------------------------');
        Program.terminal.writeLine('  {yellow-fg}help{/yellow-fg}                              Displays this manual.');
        Program.terminal.writeLine(`  {yellow-fg}start job{/yellow-fg}                         Start a job.`);
        // tslint:disable-next-line: max-line-length
        Program.terminal.writeLine('  {yellow-fg}stop job{/yellow-fg}                          Stop a job.');
        Program.terminal.writeLine('  {yellow-fg}exit{/yellow-fg}                              Gracefully closes the client app (disconnecting if needed).');
        Program.terminal.writeLine('---------------------------');
        Program.terminal.writeLine();
        Program.terminal.writeLine();
    }

}

Bootstrapper.start(Program.main);
