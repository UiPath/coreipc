import path from 'path';

export class NodeInteropPaths {
    public static getDirectoryPath(): string {
        const relativePathTargetDir =
            process.env['NodeJS_NetCoreAppTargetDir_RelativePath']
            ?? path.join('dotnet', 'UiPath.CoreIpc.NodeInterop', 'bin', 'Debug', 'net5.0');
        return path.join(process.cwd(), relativePathTargetDir);
    }

    public static getEntryPointFilePath(): string {
        return path.join(
            NodeInteropPaths.getDirectoryPath(),
            NodeInteropPaths.getEntryPointFileName());
    }

    private static getEntryPointFileName(): string {
        return 'UiPath.CoreIpc.NodeInterop.dll';
    }
}
