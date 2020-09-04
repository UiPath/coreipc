import path from 'path';

export class NodeInteropPaths {
    public static getDirectoryPath(): string {
        const relativePathTargetDir =
            process.env['NodeJS_NetCoreAppTargetDir_RelativePath']
            ?? 'dotnet\\UiPath.CoreIpc.NodeInterop\\bin\\Debug\\netcoreapp3.1';
        return path.join(process.cwd(), relativePathTargetDir);
    }

    public static getExeFilePath(): string {
        return path.join(
            NodeInteropPaths.getDirectoryPath(),
            NodeInteropPaths.getExeFileName());
    }

    private static getExeFileName(): string {
        return 'UiPath.CoreIpc.NodeInterop.exe';
    }
}
