import { spawn } from 'child_process';
import { Writable } from 'stream';

import {
    InvalidOperationError,
    PromiseCompletionSource,
} from '../../../src/std';

export class NpmProcess {
    public static runAsync(pathCwd: string, script: string): Promise<void> {
        const stderrLog = new Array<string>();
        const stdoutLog = new Array<string>();
        let stdin: Writable | null = null;

        const pcsExit = new PromiseCompletionSource<void>();

        let processExitCode: number | null;
        let processExitError: Error | undefined;

        const process = spawn('npm', ['run', script], {
            shell: true,
            cwd: pathCwd,
            stdio: 'pipe',
        });

        process.once('close', (code) => {
            processExitCode = code;

            if (code) {
                processExitError = new InvalidOperationError(
                    `Process ${process?.pid} exited with code ${code}\r\n\r\n` +
                        `$STDERR:\r\n\r\n${stderrLog.join('\r\n')}\r\n\r\n` +
                        `$STDOUT:\r\n\r\n${stdoutLog.join('\r\n')}`
                );
            }

            console.log(
                '***** SPAWNED dotnet returned code ===',
                code,
                '\r\n\r\nprocessExitError is\r\n\r\n',
                processExitError
            );

            if (processExitError) {
                pcsExit.trySetFaulted(processExitError);
            } else {
                pcsExit.trySetResult();
            }
        });

        process.stderr.setEncoding('utf-8').observeLines(stderrLog);
        process.stdout.setEncoding('utf-8').observeLines(stdoutLog);
        stdin = process.stdin.setDefaultEncoding('utf-8');

        return pcsExit.promise;
    }
}
