import { AggregateError, UnknownError, Address } from '../../../src/std';
import { DotNetProcess, Signal, Paths, InteropAddress } from '.';

export class CoreIpcServer {
    public static async host(address: Address, action: () => Promise<void>): Promise<void> {
        const runner = await CoreIpcServer.start(address);
        try {
            let error: Error | undefined;
            try {
                await Promise.race([action(), runner._dotNetScript.signalExit]);
            } catch (err) {
                error = UnknownError.ensureError(err);
            }

            console.log('**** RACE COMPLETE');

            await runner.disposeAsync();
            if (runner.processExitError && error) {
                error = new AggregateError(undefined, runner.processExitError, error);
            }

            error = error ?? (runner.processExitCode ? runner.processExitError : undefined);
            if (error) { throw error; }
        } finally {
            console.log('***** Finished hosting');
        }
    }

    private static async start(address: Address): Promise<CoreIpcServer> {
        const interopAddress = InteropAddress.from(address);

        const dotNetScript = new DotNetProcess(
            Paths.absoluteTargetDir,
            Paths.entryPoint,
            ...interopAddress.commandLineArgs(),
        );
        await dotNetScript.waitForSignal(Signal.Kind.ReadyToConnect);

        return new CoreIpcServer(dotNetScript);
    }

    private constructor(private readonly _dotNetScript: DotNetProcess) { }

    public get processExitCode(): number | null { return this._dotNetScript.processExitCode; }
    public get processExitError(): Error | undefined { return this._dotNetScript.processExitError; }

    private disposeAsync(): Promise<void> { return this._dotNetScript.disposeAsync(); }
}
