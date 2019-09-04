import * as readline from 'readline';
export { };

const readlineIface = readline.createInterface(process.stdin, process.stdout);

export class Bootstrapper {
    public static start(main: (args: string[]) => Promise<void>): void {
        (async () => {
            try {
                try {
                    await main(process.argv);
                } finally {
                    readlineIface.close();
                }
                console.debug(`main ended successfully`);
            } catch (error) {
                console.debug(`main ended with error`, error);
            }
        })();
    }
}

declare global {
    interface Console {
        readlineAsync(question?: string): Promise<string>;
    }
}

console.readlineAsync = async function(question?: string): Promise<string> {
    return new Promise<string>(resolve => {
        readlineIface.question(question || '', resolve);
    });
};
