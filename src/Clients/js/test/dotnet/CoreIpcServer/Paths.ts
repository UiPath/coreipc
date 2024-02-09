import path from 'path';

export class Paths {
    public static get absoluteTargetDir(): string {
        const relativeTargetDir =
            process.env[Paths.NodeJS_NetCoreAppTargetDir_RelativePath] ??
            path.join(
                Paths.DotNet,
                Paths.UiPath_CoreIpc_NodeInterop,
                Paths.Bin,
                Paths.Debug,
                Paths.Net60
            );

        const absoluteTargetDir = path.join(process.cwd(), relativeTargetDir);

        return absoluteTargetDir;
    }

    public static get entryPoint(): string {
        return path.join(Paths.absoluteTargetDir, Paths.UiPath_CoreIpc_NodeInterop_dll);
    }

    private static get UiPath_CoreIpc_NodeInterop_dll(): string {
        return `${Paths.UiPath_CoreIpc_NodeInterop}.dll`;
    }

    private static readonly NodeJS_NetCoreAppTargetDir_RelativePath = 'NodeJS_NetCoreAppTargetDir_RelativePath';
    private static readonly UiPath_CoreIpc_NodeInterop = 'UiPath.CoreIpc.NodeInterop';
    private static readonly DotNet = 'dotnet';
    private static readonly Bin = 'bin';
    private static readonly Debug = 'Debug';
    private static readonly Net60 = 'net6.0';
}

