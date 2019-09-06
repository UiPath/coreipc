import * as Contract from './contract';
import * as Registry from 'winreg';

import { Bootstrapper } from './bootstrapper';
import { IpcClient, Message, PromisePal, PromiseCompletionSource, CancellationToken, TimeSpan, Timeout } from '@uipath/ipc';
import { TerminalBase } from './terminal-base';

class Callback implements Contract.ICallback {
    public fail = false;

    constructor(private readonly _write: (line: string) => void) { }
    public async AddAsync(a: number, b: number): Promise<number> {
        this._write(`   => callback AddAsync({yellow-fg}${a}{/yellow-fg}, {yellow-fg}${b}{/yellow-fg})....`);
        if (!this.fail) {
            const result = a + b;
            this._write(`  returning {yellow-fg}${result}{/yellow-fg}\r\n`);
            return result;
        } else {
            const error = new Error('Mock error');
            this._write(`  throwing {yellow-fg}${error.message}{/yellow-fg}\r\n`);
            throw error;
        }
    }
    public async TimeAsync(info: string): Promise<string> {
        this._write(`   => callback TimeAsync({yellow-fg}"${info}"{/yellow-fg})....`);
        if (!this.fail) {
            const result = 'got it!';
            this._write(`  returning {yellow-fg}"${result}"{/yellow-fg}\r\n`);
            return result;
        } else {
            const error = new Error('Mock error');
            this._write(`  throwing {yellow-fg}${error.message}{/yellow-fg}\r\n`);
            throw error;
        }
    }
}
export class Program {

    private static quitRequested = false;
    private static readonly terminal = new TerminalBase(settings => {
        settings.title = 'UiPath Ipc Demo Client';
    });
    private static readonly callback = new Callback(Program.terminal.write.bind(Program.terminal));
    private static maybeClient: IpcClient<Contract.IService> | null = null;
    private static command: string | null = null;

    public static async main(args: string[]): Promise<void> {
        Program.terminal.initialize('');

        Program.help();
        while (!Program.quitRequested) {
            Program.command = await Program.terminal.readLine();
            const parts = Program.command.split(' ');

            switch (parts.length) {
                case 1:
                    switch (parts[0].toLowerCase()) {
                        case 'help':
                            Program.help();
                            break;
                        case 'status':
                            Program.status();
                            break;
                        case 'connect':
                            await Program.connect();
                            break;
                        case 'disconnect':
                            await Program.disconnect();
                            break;
                        case 'call':
                            await Program.call();
                            break;
                        case 'start':
                            await Program.start();
                            break;
                        case 'quit':
                            await Program.quit();
                            break;
                        case 'fail':
                            Program.callback.fail = true;
                            Program.terminal.writeLine('callback.fail is now set to {yellow-fg}true{/yellow-fg}');
                            break;
                        case 'installed':
                            await Program.installed();
                            break;
                        case 'succeed':
                            Program.callback.fail = false;
                            Program.terminal.writeLine('callback.fail is now set to {yellow-fg}false{/yellow-fg}');
                            break;
                    }
                    break;
                case 2:
                    switch (parts[0].toLowerCase()) {
                        case 'installed':
                            await Program.installed(parts[1]);
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
        Program.terminal.writeLine('  {yellow-fg}quit{/yellow-fg}                              Gracefully closes the client app (disconnecting if needed).');
        Program.terminal.writeLine('---------------------------');
        Program.terminal.writeLine();
        Program.terminal.writeLine();
    }
    public static getStatus(x: IpcClient<Contract.IService> | null): x is IpcClient<Contract.IService> {
        return !!x;
    }
    public static status(): void {
        if (Program.getStatus(Program.maybeClient)) {
            Program.terminal.writeLine(`client.state = Logically connected to {yellow-fg}"${Program.maybeClient.pipeName}"{/yellow-fg}`);
        } else {
            Program.terminal.writeLine(`client.state = Logically disconnected`);
        }
        if (Program.callback.fail) {
            Program.terminal.writeLine('callback.fail = {yellow-fg}true{/yellow-fg}');
        } else {
            Program.terminal.writeLine('callback.fail = {yellow-fg}false{/yellow-fg}');
        }
    }

    private static createIpcClient(pipeName: string): IpcClient<Contract.IService> {
        const x = new IpcClient(pipeName, Contract.IService, config => {
            config.callbackService = Program.callback;

            config.setBeforeConnect(async cancellationToken => {
                console.log(`BeforeConnect. Sleeping 2 seconds...`);
                await PromisePal.delay(TimeSpan.fromSeconds(2), cancellationToken);
                console.log(`Done`);
            }).setBeforeCall(async (callInfo, cancellationToken) => {
                console.log(`BeforeCall. callInfo.newConnection === ${callInfo.newConnection}`);
                await callInfo.proxy.StartTimerAsync(new Message<void>());
            });
        });
        return x;
    }

    public static async connect(): Promise<void> {
        if (Program.getStatus(Program.maybeClient)) {
            Program.terminal.writeLine(`{red-fg}You're already connected. You need to "disconnect" first.{/red-fg}`);
        } else {
            Program.terminal.writeLine('  pipe name (leave blank for "foo-pipe"): ');
            const pipeName = await Program.terminal.readLine(x => x || 'foo-pipe');
            Program.maybeClient = Program.createIpcClient(pipeName);
            //  new IpcClient(pipeName, Contract.IService, config => {
            //     config.callbackService = Program.callback;
            // });
            Program.terminal.writeLine(`You are now logically connected to "${pipeName}"`);
        }
    }
    // tslint:disable-next-line: no-shadowed-variable
    public static async disconnect(): Promise<void> {
        if (!Program.getStatus(Program.maybeClient)) {
            Program.terminal.writeLine(`{red-fg}You're not connected. You need to "connect" first.{/red-fg}`);
        } else {
            await Program.maybeClient.closeAsync();
            Program.maybeClient = null;
            Program.terminal.writeLine(`You're now logically disconnected.`);
        }
    }
    public static async call(): Promise<void> {
        if (!Program.getStatus(Program.maybeClient)) {
            Program.terminal.writeLine(`{red-fg}You're not connected. You need to "connect" first.{/red-fg}`);
        } else {
            const a = new Contract.Complex(1, 2);
            const b = new Contract.Complex(3, 4);

            Program.terminal.writeLine(`Calling AddAsync(a: ${JSON.stringify(a)}, b: ${JSON.stringify(b)})`);
            try {
                const c = await Program.maybeClient.proxy.AddAsync(a, new Message(b, TimeSpan.fromSeconds(5)));
                Program.terminal.writeLine(`Result is ${JSON.stringify(c)}`);
            } catch (error) {
                Program.terminal.writeLine(`{red-fg}Caught error: ${error}{/red-fg}`);
            }
        }
    }
    public static async start(): Promise<void> {
        if (!Program.getStatus(Program.maybeClient)) {
            Program.terminal.writeLine(`{red-fg}You're not connected. You need to "connect" first.{/red-fg}`);
        } else {
            const message = new Message<void>(TimeSpan.fromSeconds(10));

            Program.terminal.writeLine(`Calling StartTimerAsync(message: ${JSON.stringify(message)})`);
            try {
                await Program.maybeClient.proxy.StartTimerAsync(message);
                Program.terminal.writeLine(`Succeeded.`);
            } catch (error) {
                Program.terminal.writeLine(`{red-fg}Caught error: ${error}{/red-fg}`);
            }
        }
    }
    public static async quit(): Promise<void> {
        if (Program.getStatus(Program.maybeClient)) {
            await Program.disconnect();
        }

        Program.terminal.writeLine(`Quitting...`);
        await PromisePal.delay(TimeSpan.fromMilliseconds(1000));

        Program.terminal.dispose();
        Program.quitRequested = true;
    }

    public static async installed(serviceName?: string): Promise<void> {
        if (!serviceName) {
            Program.terminal.writeLine('  service name (leave blank to cancel): ');
            serviceName = await Program.terminal.readLine();
            if (!serviceName) {
                Program.terminal.writeLine('  Command canceled...');
                return;
            }
        }

        const pcs = new PromiseCompletionSource<boolean>();
        const key = new Registry({
            hive: Registry.HKLM,
            key: `\\SYSTEM\\CurrentControlSet\\Services\\${serviceName}`
        });
        key.keyExists((error, exists) => {
            if (error) {
                pcs.setError(error);
            } else {
                pcs.setResult(exists);
            }
        });

        try {
            const exists = await pcs.promise;
            Program.terminal.writeLine(`Service {yellow-fg}${serviceName}{/yellow-fg} ${exists ? '{green-fg}is{/green-fg}' : '{red-fg}is not{/red-fg}'} installed`);
        } catch (error) {
            Program.terminal.writeLine(`{red-fg}${error.message}{/red-fg}`);
        }
    }

}

Bootstrapper.start(Program.main);
