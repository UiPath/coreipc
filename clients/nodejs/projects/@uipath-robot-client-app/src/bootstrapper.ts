export { };

export class Bootstrapper {
    public static start(main: (args: string[]) => Promise<void>): void {
        (async () => {
            try {
                try {
                    await main(process.argv);
                } finally {
                    // readlineIface.close();
                }
                console.debug(`main ended successfully`);
            } catch (error) {
                console.debug(`main ended with error`, error);
            }
        })();
    }
}
