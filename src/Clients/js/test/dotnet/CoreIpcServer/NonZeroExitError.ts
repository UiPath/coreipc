import { ChildProcess } from 'child_process';
import cliColor from 'cli-color';

export class NonZeroExitError extends Error {
    constructor(
        private readonly _process: ChildProcess,
        private readonly _stdout: string,
        private readonly _stderr: string,
    ) {
        super();
    }

    public prettyPrint(): void {
        console.group(
            cliColor.bold.whiteBright(`🛑 Process `),
            cliColor.bold.yellowBright(JSON.stringify(this._process.spawnargs)),
            cliColor.bold.whiteBright('with PID'),
            cliColor.bold.yellowBright(this._process.pid),
            cliColor.bold.whiteBright('exited with code'),
            cliColor.bold.redBright(this._process.exitCode),
        );

        console.group(cliColor.bold.whiteBright(`🪵 STDOUT`));
        if (this._stdout) {
            console.log(this._stdout);
        }
        console.groupEnd();

        console.group(cliColor.bold.whiteBright(`🪵 STDERR`));
        if (this._stderr) {
            console.log(this._stderr);
        }
        console.groupEnd();

        console.groupEnd();
    }
}
