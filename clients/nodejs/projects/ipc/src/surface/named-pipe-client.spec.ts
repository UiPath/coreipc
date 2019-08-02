import { NamedPipeClientBuilder } from './named-pipe-client';
import { Message } from './message';
import { PromiseHelper } from '@uipath/ipc-helpers';
import * as child_process from 'child_process';
import * as fs from 'fs';

class IChatService {
    public HeaderAsync(title: string): Promise<void> { throw null; }
    public Fail1Async(): Promise<any> { throw null; }
    public Fail2Async(): Promise<any> { throw null; }
    public Fail3Async(): Promise<any> { throw null; }
    public TerminateAsync(): Promise<void> { throw null; }
    public SendAsync(id: number, message: Message<string>): Promise<number> { throw null; }

    public SumAsync(x: number, y: number): Promise<number> { throw null; }
    public MultiplyAsync(x: number, y: number): Promise<number> { throw null; }
}
interface IChatCallback {
    ReceiveAsync(id: number, text: Message<string>): Promise<number>;
}
// tslint:disable-next-line: max-classes-per-file
class ChatCallback implements IChatCallback {
    public async ReceiveAsync(id: number, text: Message<string>): Promise<number> {
        return id + text.Payload.length;
    }
}

function range(start: number, length: number): number[] {
    const array = new Array<number>();
    const lastExclusive = start + length;
    for (let v = start; v < lastExclusive; v++) {
        array.push(v);
    }
    return array;
}

// tslint:disable-next-line: max-classes-per-file
class CallInfo {
    public static create(index: number, proxy: IChatService): CallInfo {
        const message = new Message(`Hello .NET! (${index})`);
        message.TimeoutInSeconds = 10 * 60;

        const promise = proxy.SendAsync(index, message);

        return new CallInfo(promise, index, message.Payload);
    }

    public result: number | null = null;
    public error: Error | null = null;

    constructor(
        public readonly promise: Promise<number>,
        public readonly index: number,
        public readonly message: string
    ) {
        promise.then(
            x => this.result = x,
            x => this.error = x as any);
    }
}

describe('NamedPipeClient', () => {

    function generateGuid(): string {
        let result;
        let i;
        let j;
        result = '';
        for (j = 0; j < 32; j++) {
            if (j === 8 || j === 12 || j === 16 || j === 20) {
                result = result + '-';
            }
            i = Math.floor(Math.random() * 16).toString(16).toUpperCase();
            result = result + i;
        }
        return result;
    }

    test('end-2-end with callbacks', async () => {
        const pipeName = generateGuid();
        const serverPath = '.\\IpcSampleServerForNodejs\\included-bin\\Debug\\net461\\IpcSampleServerForNodejs.exe';

        console.log(`***** server path is ${serverPath}`);

        if (!fs.existsSync(serverPath)) {
            const errorMessage = `Could not find "${serverPath}". Make sure the IpcSampleServerForNodejs project is built.`;
            console.error(errorMessage);
            throw new Error(errorMessage);
        }

        const serverProcess = child_process.spawn(serverPath, [pipeName], {
            windowsHide: false,
            shell: true,
            detached: true
        });
        try {
            expect(serverProcess).toBeTruthy();
            await PromiseHelper.delay(1000);

            const client = await NamedPipeClientBuilder.createWithCallbacksAsync(pipeName, new IChatService(), new ChatCallback());

            await client.proxy.HeaderAsync(`Batch ${new Date()}`);

            const infos = range(0, 500)
                .map(index => CallInfo.create(index, client.proxy));

            await PromiseHelper.whenAll(...infos.map(info => info.promise));

            for (const info of infos) {
                const expected = info.index + info.message.length;
                expect(info.error).toBeFalsy();
                expect(info.result).toEqual(expected);
            }

            await expect(client.proxy.Fail1Async()).rejects.toBeDefined();
            await expect(client.proxy.Fail2Async()).rejects.toBeDefined();
            await expect(client.proxy.Fail3Async()).rejects.toBeDefined();

            await client.disposeAsync();
        } finally {
            child_process.exec(`taskkill /pid ${serverProcess.pid} /T /F`);
        }
    }, 6 * 10 * 1000);

    test('end-2-end without callbacks', async () => {
        const pipeName = generateGuid();
        const serverPath = '.\\IpcSampleServerForNodejs\\included-bin\\Debug\\net461\\IpcSampleServerForNodejs.exe';
        if (!fs.existsSync(serverPath)) {
            const errorMessage = `Could not find "${serverPath}". Make sure the IpcSampleServerForNodejs project is built.`;
            console.error(errorMessage);
            throw new Error(errorMessage);
        }

        const serverProcess = child_process.spawn(serverPath, [pipeName], {
            windowsHide: false,
            shell: true,
            detached: true
        });
        try {
            expect(serverProcess).toBeTruthy();
            await PromiseHelper.delay(1000);

            const client = await NamedPipeClientBuilder.createAsync(pipeName, new IChatService());

            await expect(client.proxy.SumAsync(10, 20)).resolves.toBe(30);
            await expect(client.proxy.MultiplyAsync(10, 20)).resolves.toBe(200);

            await client.disposeAsync();
        } finally {
            child_process.exec(`taskkill /pid ${serverProcess.pid} /T /F`);
        }
    });

});
