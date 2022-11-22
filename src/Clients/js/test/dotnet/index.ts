import { CoreIpcServer, NpmProcess } from './CoreIpcServer';
import { BrowserWebSocketAddress } from '../../src/web';
import cliColor from 'cli-color';
import commandLineArgs from 'command-line-args';
import { NonZeroExitError } from './CoreIpcServer/NonZeroExitError';

async function main(args: string[]): Promise<number> {
    // const keyPathNpmPackagejson = 'npm_package_json';
    // const pathNpmPackageJson = process.env[keyPathNpmPackagejson];

    // console.log(`🎂 process.cwd === `, process.cwd());

    // if (!pathNpmPackageJson) {
    //     console.error(`Expecting the "${keyPathNpmPackagejson}" environment variable to be set.`);
    //     return 1;
    // }

    const pathHome = process.cwd(); // path.dirname(pathNpmPackageJson);

    const headOptions = commandLineArgs([{ name: 'command', defaultOption: true }], {
        argv: args,
        stopAtFirstUnknown: true,
    });

    const tailArgs = headOptions._unknown ?? [];

    switch (headOptions.command) {
        case 'host': {
            const keyWebsocketUrl = 'websocket-url';
            const keyScript = 'script';

            const hostOptions = commandLineArgs(
                [
                    {
                        name: keyWebsocketUrl,
                        alias: 'w',
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
            const script = hostOptions[keyScript] as string;

            try {
                await CoreIpcServer.host(new BrowserWebSocketAddress(websocketUrl), async () => {
                    await NpmProcess.runAsync('𝒏𝒑𝒎 𝒓𝒖𝒏', pathHome, script);
                });
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
                process.exit(2);
            }
        }
        default: {
            return 2;
        }
    }
}

async function bootstrapper() {
    process.exit(await main(process.argv));
}

bootstrapper();
