import { spawn } from 'child_process';
import { Trace } from '@uipath/ipc';

/* @internal */
export class Utils {
    public static get userName(): string { return (process.env.userName || '').toLowerCase(); }
    public static get userDomainName(): string { return (process.env.userDomain || '').toLowerCase(); }
    public static getPipeName(sessionZeroMode: boolean): string {
        const result = sessionZeroMode ? 'RobotEndpoint' : `RobotEndpoint_${Utils.userDomainName}\\${Utils.userName}`;
        Trace.log(`getPipeName returns "${result}"`);
        return result;
    }

    private static get serviceDirectoryPath(): string { return `E:\\develop\\Studio\\Output\\bin\\Debug`; }
    private static get serviceFilePath(): string { return `${Utils.serviceDirectoryPath}\\UiPath.Service.UserHost.exe`; }

    public static spawnService(): void {
        spawn(Utils.serviceFilePath, { cwd: Utils.serviceDirectoryPath, detached: true, shell: false }).unref();
    }
}
