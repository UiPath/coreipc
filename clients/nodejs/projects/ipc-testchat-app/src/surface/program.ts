import * as readline from 'readline';
import { PromiseCompletionSource } from '@uipath/ipc-helpers';
import { ChatSessionBuilder, IChatSession } from '@uipath/ipc-testchat-stc';
const rl = readline.createInterface(process.stdin, process.stdout);

function readlineAsync(text: string): Promise<string> {
    const pcs = new PromiseCompletionSource<string>();
    rl.question(text, answer => {
        pcs.setResult(answer);
    });
    return pcs.promise;
}

function withDefault(str: string | null, _default: string): string {
    if (str == null || str === '') {
        return _default;
    } else {
        return str;
    }
}

// tslint:disable-next-line: max-classes-per-file
class Program {

    public static async main(): Promise<void> {
        let chatSession: IChatSession | null = null;

        try {
            const pipeName = await withDefault(
                await readlineAsync('pipe name (ENTER for "test-char-server-pipe-name"): '),
                'test-char-server-pipe-name');

            const nickname = await withDefault(
                await readlineAsync('nickname (ENTER for "Jerry"): '),
                'Jerry');

            chatSession = await ChatSessionBuilder.createAsync(pipeName, nickname);

            Program.observeErrors(chatSession);
            Program.observeMessages(chatSession);

            while (true) {
                const text = await readlineAsync('YOU: ');
                if (text === 'quit') {
                    break;
                }

                await chatSession.broadcastAsync(text);
            }

        } finally {
            if (chatSession) {
                try {
                    await chatSession.disposeAsync();
                } catch (error) {
                    console.error(`received error: ${error}`);
                }
            }
        }
    }

    private static observeErrors(chatSession: IChatSession): void {
        chatSession.errors$.subscribe(error => {
            // console.error(error);
        });
    }

    private static observeMessages(chatSession: IChatSession): void {
        chatSession.sessionCreations$.subscribe(message => {
            console.warn(`SERVER: ${message.nickname} is now connected.`);
        });
        chatSession.sessionDestructions$.subscribe(message => {
            console.warn(`SERVER: ${message.nickname} has disconnected.`);
        });
        chatSession.messages$.subscribe(message => {
            console.log(`${message.nickname}: ${message.message}`);
        });
    }

}

function wrapup(): void {
    rl.close();
}

Program.main().then(
    () => {
        console.log(`Program.main() has finished successfully`);
        wrapup();
    },
    maybeError => {
        if (maybeError) {
            console.error(maybeError);
        }
        wrapup();
    }
);
