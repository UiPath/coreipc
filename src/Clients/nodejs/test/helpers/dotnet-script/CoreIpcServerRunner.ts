import * as path from 'path';
import * as fs from 'fs';

import { DotNetScript, SignalKind } from './DotNetScript';

export class CoreIpcServerRunner {
    public static async host(pipeName: string, action: () => Promise<void>): Promise<void> {
        const runner = await CoreIpcServerRunner.start(pipeName);
        try {
            await action();
        } finally {
            await runner.disposeAsync();
        }
    }

    private static async start(pipeName: string): Promise<CoreIpcServerRunner> {
        const dotNetScript = new DotNetScript(this.getDotNetOutputPath(), this.getCSharpCode(), pipeName);
        await dotNetScript.waitForSignal(SignalKind.ReadyToConnect);

        return new CoreIpcServerRunner(dotNetScript);
    }

    private constructor(private readonly _dotNetScript: DotNetScript) { }

    private disposeAsync(): Promise<void> { return this._dotNetScript.disposeAsync(); }

    private static getDotNetOutputPath(): string {
        const relativePathTargetDir =
            process.env['NodeJS_NetStandardTargetDir_RelativePath']
            ?? '..\\..\\UiPath.CoreIpc\\bin\\Debug\\netstandard2.0';
        return path.join(process.cwd(), relativePathTargetDir);
    }

    private static getCSharpCode(): string {
        const filePath = path.join(process.cwd(), 'test', 'unit', 'surface', 'core', 'CoreIpcServer.csx');
        return fs.readFileSync(filePath).toString();
    }
}
