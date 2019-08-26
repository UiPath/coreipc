import { CancellationToken, IpcClient, PromisePal } from '@uipath/ipc';
import { IComputingCallback, IComputingService, ComplexNumber, SystemMessage } from './contract';

class Program {
    public static async main(args: string[]): Promise<void> {
        const callback = new ComputingCallback('[>---Responded from Nodejs---<]');

        const client = new IpcClient('computingPipe', new IComputingService(), config => {
            config.callbackService = callback;
        });

        try {
            const x: ComplexNumber = { a: 1.41421, b: 1.61803 };
            const y: ComplexNumber = { a: 2.71828, b: 3.14159 };

            const z: ComplexNumber = await client.proxy.AddComplexNumber(x, y);

            console.log('x == ', x);
            console.log('y == ', y);
            console.log('z == ', z);

            console.log('Urmeaza sa dorm 10 secunde');
            await PromisePal.delay(10 * 1000);
            console.log('acum invocam mai departe');
        } finally {
            await client.closeAsync();
        }
    }

    private static toString(x: ComplexNumber): string {
        return `{ ${x.a}, ${x.b} }`;
    }
}

class ComputingCallback extends IComputingCallback {
    constructor(private readonly _id: string) { super(); }
    public async GetId(): Promise<string> { return this._id; }
}

Program.main(process.argv).then(
    _ => {
        console.log('Program ended successfully.');
    },
    (error: Error) => {
        console.error(`Program ended with error:\r\n\t${error}\r\n${indent(error.stack || '', 'StackTrace: ', ' '.repeat('StackTrace: '.length))}`);
    }
);

function indent(input: string, headIndent: string, tailIndent: string): string {
    return input
        .replace('\r\n', '\n')
        .split('\n')
        .map((x, index) => `${(0 === index) ? headIndent : tailIndent}${x}`)
        .reduce((sum, current) => `${sum}\r\n${current}`, '');
}
