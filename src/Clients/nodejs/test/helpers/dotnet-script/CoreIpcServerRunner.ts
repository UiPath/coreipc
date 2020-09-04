import * as path from 'path';

import { DotNetProcess, SignalKind } from './DotNetProcess';
import { AggregateError } from '../../../src/foundation/errors/AggregateError';

export class CoreIpcServerRunner {
    public static async host(pipeName: string, action: () => Promise<void>): Promise<void> {
        const runner = await CoreIpcServerRunner.start(pipeName);
        try {
            let error: Error | undefined;
            try {
                await Promise.race([action(), runner._dotNetScript.signalExit]);
            } catch (err) {
                error = err;
            }

            await runner.disposeAsync();
            if (runner.processExitError && error) {
                error = new AggregateError(undefined, runner.processExitError, error);
            }

            error = error ?? (runner.processExitCode ? runner.processExitError : undefined);
            if (error) { throw error; }
        } finally {
        }
    }

    private static async start(pipeName: string): Promise<CoreIpcServerRunner> {
        const dotNetScript = new DotNetProcess(
            CoreIpcServerRunner.getDirectoryPath(),
            CoreIpcServerRunner.getExeFilePath(),

            '--pipe', pipeName,
        );
        await dotNetScript.waitForSignal(SignalKind.ReadyToConnect);

        return new CoreIpcServerRunner(dotNetScript);
    }

    private constructor(private readonly _dotNetScript: DotNetProcess) { }

    public get processExitCode(): number | undefined { return this._dotNetScript.processExitCode; }
    public get processExitError(): Error | undefined { return this._dotNetScript.processExitError; }

    private disposeAsync(): Promise<void> { return this._dotNetScript.disposeAsync(); }

    private static getDirectoryPath(): string {
        const relativePathTargetDir =
            process.env['NodeJS_NetCoreAppTargetDir_RelativePath']
            ?? 'dotnet\\UiPath.CoreIpc.NodeInterop\\bin\\Debug\\netcoreapp3.1';
        return path.join(process.cwd(), relativePathTargetDir);
    }

    private static getExeFileName(): string {
        return 'UiPath.CoreIpc.NodeInterop.exe';
    }

    private static getExeFilePath(): string {
        return path.join(CoreIpcServerRunner.getDirectoryPath(), CoreIpcServerRunner.getExeFileName());
    }
}
