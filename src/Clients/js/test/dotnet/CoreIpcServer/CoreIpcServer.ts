import { AggregateError, UnknownError, Address } from '../../../src/std';
import { DotNetProcess, Signal, Paths, InteropAddress } from '.';

export class CoreIpcServer {
    public static async host(
        addresses: Address[],
        action: () => Promise<void>
    ): Promise<void> {
        const runner = await CoreIpcServer.start(addresses);
        try {
            let error: Error | undefined;
            try {
                await Promise.race([action(), runner._dotNetScript.signalExit]);
            } catch (err) {
                error = UnknownError.ensureError(err);
            }

            await runner.disposeAsync();
            if (runner.processExitError && error) {
                error = new AggregateError(
                    undefined,
                    runner.processExitError,
                    error
                );
            }

            error =
                error ??
                (runner.processExitCode ? runner.processExitError : undefined);
            if (error) {
                throw error;
            }
        } finally {
        }
    }

    private static async start(addresses: Address[]): Promise<CoreIpcServer> {
        const commandLineArgs = InteropAddress.computeCommandLineArgs(addresses.map(InteropAddress.from));

        const dotNetScript = new DotNetProcess(
            '𝒄𝒐𝒓𝒆𝒊𝒑𝒄 𝒔𝒆𝒓𝒗𝒆𝒓',
            Paths.absoluteTargetDir,
            Paths.entryPoint,
            ...commandLineArgs,
        );

        const details = await dotNetScript.waitForSignal<Signal.ExceptionDetails | null>(Signal.Kind.ReadyToConnect);
        if (details != null) {
            throw new Error(`The CoreIpc server couldn't test it's own connectivity. The .NET exception was "${details.Type}" with message "${details.Message}"`);
        }

        return new CoreIpcServer(dotNetScript);
    }

    private constructor(private readonly _dotNetScript: DotNetProcess) {}

    public get processExitCode(): number | null {
        return this._dotNetScript.processExitCode;
    }
    public get processExitError(): Error | undefined {
        return this._dotNetScript.processExitError;
    }

    private disposeAsync(): Promise<void> {
        return this._dotNetScript.disposeAsync();
    }
}
