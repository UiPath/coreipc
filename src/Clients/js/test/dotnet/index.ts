import { CoreIpcServer, NpmProcess } from './CoreIpcServer';
import { BrowserWebSocketAddress } from '../../src/web';
import cliColor from 'cli-color';
import commandLineArgs from 'command-line-args';
import { NonZeroExitError } from './CoreIpcServer/NonZeroExitError';
import { Address } from '../../src/std';
import { NamedPipeAddress } from '../../src/node';

async function main(args: string[]): Promise<number> {
    const pathHome = process.cwd(); // path.dirname(pathNpmPackageJson);

    const headOptions = commandLineArgs([{ name: 'command', defaultOption: true }], {
        argv: args,
        stopAtFirstUnknown: true,
    });

    const tailArgs = headOptions._unknown ?? [];

    switch (headOptions.command) {
        case 'host': {
            const keyWebsocketUrl = 'websocket-url';
            const keyPipe = 'pipe';

            const keyScript = 'script';

            const hostOptions = commandLineArgs(
                [
                    {
                        name: keyWebsocketUrl,
                        alias: 'w',
                        type: String,
                    },
                    {
                        name: keyPipe,
                        alias: 'p',
                        type: String,
                    },
                    {
                        name: keyScript,
                        alias: 's',
                        type: String,
                    },
                ],
                { argv: tailArgs },
            );

            const websocketUrl = hostOptions[keyWebsocketUrl] as string;
            const pipe = hostOptions[keyPipe] as string;
            const script = hostOptions[keyScript] as string;

            let addresses = new Array<Address>();

            if (websocketUrl) {
                addresses.push(new BrowserWebSocketAddress(websocketUrl));
            }

            if (pipe) {
                addresses.push(new NamedPipeAddress(pipe));
            }

            try {
                await CoreIpcServer.host(addresses, async () => {
                    await NpmProcess.runAsync('𝒏𝒑𝒎 𝒓𝒖𝒏', pathHome, script);
                });

                return 0;
            } catch (error) {
                function print() {
                    if (error instanceof NonZeroExitError) {
                        error.prettyPrint();
                        return;
                    }

                    const text = error instanceof Error ? error.message : `${error}`;
                    console.error(cliColor.redBright(text));
                }

                print();
                return 3;
            }
        }
        default: {
            return 2;
        }
    }
}

async function bootstrapper() {
    try {
        const code = await main(process.argv);
        process.exit(code);
    } catch (e) {
        console.error(e);
        process.exit(4);
    }
}

bootstrapper();
