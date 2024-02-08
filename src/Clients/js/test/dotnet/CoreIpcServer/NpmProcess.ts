import { spawn } from 'child_process';
import { Writable } from 'stream';
import cliColor from 'cli-color';

import { InvalidOperationError, PromiseCompletionSource } from '../../../src/std';
import { NonZeroExitError } from './NonZeroExitError';

export class NpmProcess {
    public static runAsync(label: string, pathCwd: string, script: string): Promise<void> {
        const stderrLog = new Array<string>();
        const stdoutLog = new Array<string>();
        let stdin: Writable | null = null;

        const pcsExit = new PromiseCompletionSource<void>();

        let processExitCode: number | null;
        let processExitError: Error | undefined;

        console.group(`⚡ ${cliColor.magentaBright(label)}`, script);
        console.log(`⚡ ${cliColor.magentaBright(label)}::Starting`);

        const process = spawn('npm', ['run', script], {
            shell: true,
            cwd: pathCwd,
            stdio: 'inherit',
        });

        console.log(`⚡ ${cliColor.magentaBright(label)}::Started. PID === `, process.pid);

        process.once('close', code => {
            processExitCode = code;

            if (code) {
                processExitError = new NonZeroExitError(
                    process,
                    stderrLog.join('\r\n'),
                    stderrLog.join('\r\n'),
                );
                // processExitError = new InvalidOperationError(
                //     `Process ${process?.pid} exited with code ${code}\r\n` +
                //         `$STDERR:\r\n${stderrLog.join('\r\n')}\r\n` +
                //         `$STDOUT:\r\n${stdoutLog.join('\r\n')}`,
                // );
            }

            if (!processExitError) {
                console.log(`⚡ ${cliColor.magentaBright(label)}::Succeeded`);
            } else {
                console.log(
                    `⚡ ${cliColor.magentaBright(label)}::Failed with exit code`,
                    cliColor.redBright(code),
                );
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
        //         cliColor.magentaBright(label),
        //         cliColor.redBright('.stdout:'),
        //         line
        //     );

        //     stderrLog.push(line);
        // });
        // process.stdout.setEncoding('utf-8').observeLines((line) => {
        //     console.log(
        //         cliColor.magentaBright(label),
        //         cliColor.greenBright('.stdout:'),
        //         line
        //     );

        //     stdoutLog.push(line);
        // });
        // stdin = process.stdin.setDefaultEncoding('utf-8');

        return pcsExit.promise;
    }
}
