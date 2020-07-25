import * as path from 'path';
import * as fs from 'fs';

import { DotNetScript, SignalKind } from './DotNetScript';
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
        const dotNetScript = new DotNetScript(this.getDotNetOutputPath(), this.getCSharpCode(), pipeName);
        await dotNetScript.waitForSignal(SignalKind.ReadyToConnect);

        return new CoreIpcServerRunner(dotNetScript);
    }

    private constructor(private readonly _dotNetScript: DotNetScript) { }

    public get processExitCode(): number | undefined { return this._dotNetScript.processExitCode; }
    public get processExitError(): Error | undefined { return this._dotNetScript.processExitError; }

    private disposeAsync(): Promise<void> { return this._dotNetScript.disposeAsync(); }

    private static getDotNetOutputPath(): string {
        const relativePathTargetDir =
            process.env['NodeJS_NetStandardTargetDir_RelativePath']
            ?? '..\\..\\UiPath.CoreIpc\\bin\\Debug\\netstandard2.0';
        return path.join(process.cwd(), relativePathTargetDir);
    }

    private static getCSharpCode(): string {
        const filePath = path.join(process.cwd(), 'test', 'unit', 'dotnet', 'CoreIpcServer.csx');
        return fs.readFileSync(filePath).toString();
    }
}
