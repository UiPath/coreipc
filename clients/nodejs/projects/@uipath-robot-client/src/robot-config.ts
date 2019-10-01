import * as NodePath from 'path';
import * as NodeFS from 'fs';
import { TimeSpan } from '@uipath/ipc';
// tslint:disable-next-line: variable-name
const NodeProcess = process;

class RobotConfigPal {
    constructor(private readonly _context: Readonly<IRobotEnvironmentContext>) { }

    public computeData(): IRobotEnvironmentData {
        enum DecodedServiceInstalled {
            True,
            False,
            Neither
        }
        function decodeRawServiceInstalled(x: string | undefined): DecodedServiceInstalled {
            if (!x) { return DecodedServiceInstalled.Neither; }
            switch (x.toLowerCase()) {
                case 'true':
                case '1':
                    return DecodedServiceInstalled.True;
                case 'false':
                case '0':
                    return DecodedServiceInstalled.False;
                default: return DecodedServiceInstalled.Neither;
            }
        }

        const serviceHome = this._context.environment.getVar(this._context.envvarnameServiceHome) || NodePath.normalize(
            NodePath.join(
                this._context.environment.cwd,
                this._context.relpathFailoverServiceHome));

        const myself = this;
        function computeServiceInstalled(x: DecodedServiceInstalled): boolean {
            switch (x) {
                case DecodedServiceInstalled.True: return true;
                case DecodedServiceInstalled.False: return false;
                case DecodedServiceInstalled.Neither:
                default:
                    return !myself._context.environment.fileExists(
                        NodePath.join(
                            serviceHome,
                            myself._context.filenameUserHostService));
            }
        }
        const serviceInstalled = computeServiceInstalled(
            decodeRawServiceInstalled(this._context.environment.getVar(this._context.envvarnameServiceInstalled)));

        const userName = this._context.environment.userName;
        const userDomain = this._context.environment.userDomain;
        const pipeName = serviceInstalled ? 'RobotEndpoint' : `RobotEndpoint_${userDomain}\\${userName}`.replace('\\', '@');

        const maybeUserHostServiceFilePath = serviceInstalled ? undefined : NodePath.join(serviceHome, this._context.filenameUserHostService);
        const oldAgentFilePath = NodePath.join(serviceHome, myself._context.filenameOldAgent);

        return {
            serviceHome,
            serviceInstalled,
            defaultCallTimeout: this._context.defaultCallTimeout,
            maybeUserHostServiceFilePath,
            oldAgentFilePath,
            installPackageRequestTimeout: this._context.installPackageRequestTimeout,
            userName,
            userDomain,
            pipeName
        };
    }
}

export interface IRobotEnvironment {
    fileExists(path: string): boolean;
    getVar(name: string): string | undefined;

    readonly userName: string;
    readonly userDomain: string;

    readonly cwd: string;
}

class PhysicalEnvironment implements IRobotEnvironment {
    constructor(private readonly _settings: Readonly<IRobotEnvironmentSettings>) { }

    // tslint:disable-next-line: no-shadowed-variable
    public fileExists(path: string): boolean { return NodeFS.existsSync(path); }
    public getVar(name: string): string | undefined { return NodeProcess.env[name]; }

    public get userName(): string { return (NodeProcess.env.userName || '').toLowerCase(); }
    public get userDomain(): string { return (NodeProcess.env.userDomain || '').toLowerCase(); }

    public get cwd(): string { return NodeProcess.cwd(); }
}

export class RobotConfig {
    private static _data: Readonly<IRobotEnvironmentData> = null as any;

    private static readonly _context: IRobotEnvironmentContext = (() => {
        const result: IRobotEnvironmentContext = {
            filenameUserHostService: 'UiPath.Service.UserHost.exe',
            filenameOldAgent: 'UiPath.Agent.exe',

            envvarnameServiceHome: 'UIPATH_SERVICE_HOME',
            envvarnameServiceInstalled: 'UIPATH_SERVICE_INSTALLED',
            relpathFailoverServiceHome: '..',

            defaultCallTimeout: TimeSpan.fromSeconds(40),

            installPackageRequestTimeout: TimeSpan.fromMinutes(20),

            environment: null as any
        };
        result.environment = new PhysicalEnvironment(result);
        (RobotConfig as any)._context = result;
        RobotConfig.reinitialize();
        return result;
    })();

    public static get filenameUserHostService(): string { return RobotConfig._context.filenameUserHostService; }
    public static set filenameUserHostService(value: string) {
        RobotConfig._context.filenameUserHostService = value;
        RobotConfig.reinitialize();
    }

    public static get envvarnameServiceHome(): string { return RobotConfig._context.envvarnameServiceHome; }
    public static set envvarnameServiceHome(value: string) {
        RobotConfig._context.envvarnameServiceHome = value;
        RobotConfig.reinitialize();
    }

    public static get envvarnameServiceInstalled(): string { return RobotConfig._context.envvarnameServiceInstalled; }
    public static set envvarnameServiceInstalled(value: string) {
        RobotConfig._context.envvarnameServiceInstalled = value;
        RobotConfig.reinitialize();
    }

    public static get relpathFailoverServiceHome(): string { return RobotConfig._context.relpathFailoverServiceHome; }
    public static set relpathFailoverServiceHome(value: string) {
        RobotConfig._context.relpathFailoverServiceHome = value;
        RobotConfig.reinitialize();
    }

    public static get installPackageRequestTimeout(): TimeSpan { return RobotConfig._context.installPackageRequestTimeout; }
    public static set installPackageRequestTimeout(value: TimeSpan) {
        RobotConfig._context.installPackageRequestTimeout = value;
        RobotConfig.reinitialize();
    }

    public static get defaultCallTimeout(): TimeSpan { return RobotConfig._context.defaultCallTimeout; }
    public static set defaultCallTimeout(value: TimeSpan) {
        RobotConfig._context.defaultCallTimeout = value;
        RobotConfig.reinitialize();
    }

    public static get environment(): IRobotEnvironment { return RobotConfig._context.environment; }
    public static set environment(value: IRobotEnvironment) {
        RobotConfig._context.environment = value;
        RobotConfig.reinitialize();
    }

    private static reinitialize(): void {
        RobotConfig._data = new RobotConfigPal(RobotConfig._context).computeData();
    }

    public static get data(): Readonly<IRobotEnvironmentData> {
        return RobotConfig._data;
    }
}

export interface IRobotEnvironmentSettings {
    filenameUserHostService: string;
    filenameOldAgent: string;

    relpathFailoverServiceHome: string;
    envvarnameServiceHome: string;
    envvarnameServiceInstalled: string;

    installPackageRequestTimeout: TimeSpan;
    defaultCallTimeout: TimeSpan;
}
export interface IRobotEnvironmentContext extends IRobotEnvironmentSettings {
    environment: IRobotEnvironment;
}
export interface IRobotEnvironmentData {
    readonly serviceHome: string;
    readonly serviceInstalled: boolean;

    readonly defaultCallTimeout: TimeSpan;

    readonly maybeUserHostServiceFilePath: string | undefined;
    readonly oldAgentFilePath: string;

    readonly installPackageRequestTimeout: TimeSpan;

    readonly userName: string;
    readonly userDomain: string;
    readonly pipeName: string;
}
