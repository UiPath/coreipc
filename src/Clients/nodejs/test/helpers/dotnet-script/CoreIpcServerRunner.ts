import { DotNetProcess, SignalKind } from './DotNetProcess';
import { AggregateError } from '../../../src/foundation/errors/AggregateError';
import { NodeInteropPaths } from '.';

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
            NodeInteropPaths.getDirectoryPath(),
            NodeInteropPaths.getEntryPointFilePath(),

            '--pipe', pipeName,
        );
        await dotNetScript.waitForSignal(SignalKind.ReadyToConnect);

        return new CoreIpcServerRunner(dotNetScript);
    }

    private constructor(private readonly _dotNetScript: DotNetProcess) { }

    public get processExitCode(): number | undefined { return this._dotNetScript.processExitCode; }
    public get processExitError(): Error | undefined { return this._dotNetScript.processExitError; }

    private disposeAsync(): Promise<void> { return this._dotNetScript.disposeAsync(); }
}
