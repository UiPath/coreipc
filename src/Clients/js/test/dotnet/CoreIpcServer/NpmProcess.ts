import { spawn } from 'child_process';
import { Writable } from 'stream';
import cliColor from 'cli-color';

import {
    InvalidOperationError,
    PromiseCompletionSource,
} from '../../../src/std';

export class NpmProcess {
    public static runAsync(
        label: string,
        pathCwd: string,
        script: string
    ): Promise<void> {
        const stderrLog = new Array<string>();
        const stdoutLog = new Array<string>();
        let stdin: Writable | null = null;

        const pcsExit = new PromiseCompletionSource<void>();

        let processExitCode: number | null;
        let processExitError: Error | undefined;

        console.group(`⚡ ${cliColor.magenta(label)}`, script);
        console.log(`⚡ ${cliColor.magenta(label)}::Starting`);

        const process = spawn('npm', ['run', script], {
            shell: true,
            cwd: pathCwd,
            stdio: 'inherit',
        });

        console.log(
            `⚡ ${cliColor.magenta(label)}::Started. PID === `,
            process.pid
        );

        process.once('close', (code) => {
            processExitCode = code;

            if (code) {
                processExitError = new InvalidOperationError(
                    `Process ${process?.pid} exited with code ${code}\r\n\r\n` +
                        `$STDERR:\r\n\r\n${stderrLog.join('\r\n')}\r\n\r\n` +
                        `$STDOUT:\r\n\r\n${stdoutLog.join('\r\n')}`
                );
            }

            if (!processExitError) {
                console.log(`⚡ ${cliColor.magenta(label)}::Succeeded`);
            } else {
                console.group(`⚡ ${cliColor.magenta(label)}::Failed`);
                console.log('exitcode === ', code);
                console.log('error ===\r\n', processExitError);
                console.groupEnd();
            }
            console.groupEnd();

            if (processExitError) {
                pcsExit.trySetFaulted(processExitError);
            } else {
                pcsExit.trySetResult();
            }
        });

        // process.stderr.setEncoding('utf-8').observeLines((line) => {
        //     console.error(
        //         cliColor.magenta(label),
        //         cliColor.redBright('.stdout:'),
        //         line
        //     );

        //     stderrLog.push(line);
        // });
        // process.stdout.setEncoding('utf-8').observeLines((line) => {
        //     console.log(
        //         cliColor.magenta(label),
        //         cliColor.greenBright('.stdout:'),
        //         line
        //     );

        //     stdoutLog.push(line);
        // });
        // stdin = process.stdin.setDefaultEncoding('utf-8');

        return pcsExit.promise;
    }
}
