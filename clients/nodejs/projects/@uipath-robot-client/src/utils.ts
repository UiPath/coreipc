import { spawn } from 'child_process';

/* @internal */
export class Utils {

    public static get userName(): string { return (process.env.userName || '').toLowerCase(); }
    public static get userDomainName(): string { return (process.env.userDomain || '').toLowerCase(); }
    public static get pipeName(): string { return `RobotEndpoint_${Utils.userDomainName}\\${Utils.userName}`; }

    private static get serviceDirectoryPath(): string { return `E:\\develop\\Studio\\Output\\bin\\Debug`; }
    private static get serviceFilePath(): string { return `${Utils.serviceDirectoryPath}\\UiPath.Service.UserHost.exe`; }

    public static spawnService(): void {
        spawn(Utils.serviceFilePath, { cwd: Utils.serviceDirectoryPath, detached: true, shell: false }).unref();
    }
}
