import { Bootstrapper } from './bootstrapper';
import { IpcClient, Message } from '@uipath/ipc';
import * as Contract from './contract';

export class Program {
    public static async main(args: string[]): Promise<void> {
        console.log();
        console.log('********************');
        const pipeName = await console.readlineAsync('Pipe name: ');

        const ipcClient = new IpcClient(pipeName, Contract.IService, config => {
            config.callbackService = new Callback();
        });
        try {
            const a = new Contract.Complex(1, 2);
            const b = new Contract.Complex(3, 4);

            console.log('Calling AddAsync(', a, ', ', b, ')');
            const result = await ipcClient.proxy.AddAsync(a, new Message(b, 1));

            console.log('result === ', result);
        } finally {
            await ipcClient.closeAsync();
        }
    }
}

class Callback implements Contract.ICallback {

    public async AddAsync(a: number, b: number): Promise<number> {
        return a + b;
    }

}

Bootstrapper.start(Program.main);
