import { TerminalBase } from './terminal-base';
import { Bootstrapper } from './bootstrapper';
import { robotAgentProxyCtor } from '@uipath/robot-client';

export class Program {
    private static readonly terminal = new TerminalBase(settings => {
        settings.title = 'UiPath Ipc Demo Client';
    });
    private static exitRequested = false;
    private static command: string | null = null;

    public static async main(args: string[]): Promise<void> {
        Program.terminal.initialize('');

        const x = new robotAgentProxyCtor();
        x.ProcessListUpdated.subscribe(args => {
            // args.Processes[0]
        });

        Program.help();
        while (!Program.exitRequested) {
            Program.command = await Program.terminal.readLine();
            const parts = Program.command.split(' ');

            switch (parts.length) {
                case 1:
                    switch (parts[0].toLowerCase()) {
                        case 'exit':
                            Program.terminal.dispose();
                            Program.exitRequested = true;
                            break;
                        case 'help':
                            Program.help();
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
        Program.terminal.writeLine(`  {yellow-fg}status{/yellow-fg}                            Show the current status (connected or not and fail/succeed for callbacks).`);
        // tslint:disable-next-line: max-line-length
        Program.terminal.writeLine('  {yellow-fg}connect{/yellow-fg}                           Logically connects the client to the server (the actual connection will occur with the first remote call).');
        Program.terminal.writeLine('  {yellow-fg}disconnect{/yellow-fg}                        Disconnects the client from the server.');
        Program.terminal.writeLine('  {yellow-fg}call{/yellow-fg}                              Calls the AddAsync method.');
        Program.terminal.writeLine('  {yellow-fg}fail{/yellow-fg}                              Set up the callback service so that it throws when called.');
        Program.terminal.writeLine('  {yellow-fg}succeed{/yellow-fg}                           Set up the callback service so that it succeeds when called (default).');
        Program.terminal.writeLine('  {yellow-fg}start{/yellow-fg}                             Tell the server to start calling the client back every second.');
        Program.terminal.writeLine('  {yellow-fg}installed{/yellow-fg} [serviceName]           Checks if a Windows Service is installed.');
        Program.terminal.writeLine('  {yellow-fg}exit{/yellow-fg}                              Gracefully closes the client app (disconnecting if needed).');
        Program.terminal.writeLine('---------------------------');
        Program.terminal.writeLine();
        Program.terminal.writeLine();
    }

}

Bootstrapper.start(Program.main);
