import { execFile, ExecFileOptions } from 'child_process';
import * as fs from 'fs';
import { randomBytes } from 'crypto';
import {
    PromiseCompletionSource,
    CancellationToken,
    CancellationTokenSource
} from '../src/index';

const dotnetScript = `${process.cwd()}\\$tools\\dotnet-script\\dotnet-script.cmd`;

// tslint:disable-next-line: max-line-length
async function executeCSharp(scriptOrPath: string, cancellationToken: CancellationToken, isPath?: boolean, waitForLine?: string, pcsLine?: PromiseCompletionSource<void>): Promise<void> {
    const directoryPath = `${process.cwd()}\\$uipath-ipc-dotnet`;
    const pcs = new PromiseCompletionSource<void>();
    const options: ExecFileOptions = {
        cwd: directoryPath
    };

    const csxPath = `${directoryPath}\\${randomBytes(16).toString('base64').replace('/', '')}.csx`;
    const pcsScriptFileReady = new PromiseCompletionSource<void>();
    if (isPath) {
        fs.copyFile(scriptOrPath, csxPath, error => {
            if (!error) {
                pcsScriptFileReady.setResult(undefined);
            } else {
                pcsScriptFileReady.setError(error);
            }
        });
    } else {
        fs.writeFile(csxPath, scriptOrPath, error => {
            if (!error) {
                pcsScriptFileReady.setResult(undefined);
            } else {
                pcsScriptFileReady.setError(error);
            }
        });
    }
    await pcsScriptFileReady.promise;

    try {
        const childProcess = execFile(dotnetScript, [csxPath], options, (error, stdout, stderr) => {
            if (error) {
                pcs.trySetError(error);
            } else if (stderr) {
                pcs.trySetError(new Error(stderr));
            } else {
                pcs.trySetResult(undefined);
            }
        });
        if (waitForLine && pcsLine) {
            childProcess.stdout.addListener('data', chunk => {
                const str = `${chunk}`.trim();
                if (waitForLine === str) {
                    pcsLine.trySetResult(undefined);
                }
            });
        }
        const registration = cancellationToken.register(() => {
            pcs.trySetResult(undefined);
            try {
                childProcess.kill();
            } catch (error) {
            }
        });
        try {
            return await pcs.promise;
        } finally {
            registration.dispose();
        }
    } finally {
        fs.unlinkSync(csxPath);
    }
}

export async function runCsxFrom<T>(relativePath: string, func: () => Promise<T>): Promise<T> {
    const cts = new CancellationTokenSource();
    try {
        const path = `${process.cwd()}\\projects\\@uipath-ipc\\test\\${relativePath}`;
        const pcsReady = new PromiseCompletionSource<void>();
        executeCSharp(path, cts.token, true, '#!READY', pcsReady);
        await pcsReady.promise;

        return await func();
    } finally {
        cts.cancel();
    }
}

export async function runCsx<T>(csx: string, func: () => Promise<T>): Promise<T> {
    const cts = new CancellationTokenSource();
    try {
        const pcsReady = new PromiseCompletionSource<void>();
        executeCSharp(csx, cts.token, false, '#!READY', pcsReady);
        await pcsReady.promise;

        return await func();
    } finally {
        cts.cancel();
    }
}
