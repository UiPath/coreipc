import * as NodePath from 'path';
import * as NodeFS from 'fs';
// tslint:disable-next-line: variable-name
const NodeProcess = process;

export class RobotEnvironmentStore {
    public static initialize(partialContext?: Partial<IRobotEnvironmentContext>): void {
        const context = RobotEnvironmentStore.coallesceToDefaults(partialContext || {});
        const data = new RobotEnvironmentComputer(context).compute();
        RobotEnvironmentStore._getData = () => data;
    }

    private static _getData: () => Readonly<IRobotEnvironmentData> = () => {
        throw new Error(`You must call RobotEnvironmentStore.initialize(...) before accessing RobotEnvironmentStore.data`);
    }
    public static get data(): Readonly<IRobotEnvironmentData> { return RobotEnvironmentStore._getData(); }

    private static coallesceToDefaults(partialContext: Partial<IRobotEnvironmentContext>): Readonly<IRobotEnvironmentContext> {
        const result = {
            filenameUserHostService: partialContext.filenameUserHostService || 'UiPath.Service.UserHost.exe',
            relpathFailoverServiceHome: partialContext.relpathFailoverServiceHome || '..',
            envvarnameServiceHome: partialContext.envvarnameServiceHome || 'UIPATH_SERVICE_HOME',
            envvarnameServiceInstalled: partialContext.envvarnameServiceInstalled || 'UIPATH_SERVICE_INSTALLED',
            environment: partialContext.environment || null as any as IEnvironment
        };
        result.environment = result.environment || new PhysicalEnvironment(result);
        return result;
    }
}

export interface IRobotEnvironmentSettings {
    filenameUserHostService: string;
    relpathFailoverServiceHome: string;
    envvarnameServiceHome: string;
    envvarnameServiceInstalled: string;
}
export interface IRobotEnvironmentContext extends IRobotEnvironmentSettings {
    environment: IEnvironment;
}

export interface IRobotEnvironmentData {
    readonly serviceHome: string;
    readonly serviceInstalled: boolean;
}

export interface IEnvironment {
    fileExists(path: string): boolean;
    getVar(name: string): string | undefined;

    readonly cwd: string;
}

class PhysicalEnvironment implements IEnvironment {
    constructor(private readonly _settings: Readonly<IRobotEnvironmentSettings>) { }

    // tslint:disable-next-line: no-shadowed-variable
    public fileExists(path: string): boolean { return NodeFS.existsSync(path); }
    public getVar(name: string): string | undefined { return NodeProcess.env[name]; }

    public get cwd(): string { return NodeProcess.cwd(); }
}

class RobotEnvironmentComputer {
    constructor(private readonly _settings: Readonly<IRobotEnvironmentContext>) { }

    public compute(): IRobotEnvironmentData {
        enum DecodedServiceInstalled {
            True,
            False,
            Neither
        }
        function decodeRawServiceInstalled(x: string | undefined): DecodedServiceInstalled {
            if (!x) { return DecodedServiceInstalled.Neither; }
            switch (x.toLowerCase()) {
                case 'true': return DecodedServiceInstalled.True;
                case 'false': return DecodedServiceInstalled.False;
                default: return DecodedServiceInstalled.Neither;
            }
        }

        const serviceHome = this._settings.environment.getVar(this._settings.envvarnameServiceHome) || NodePath.normalize(
            NodePath.join(
                this._settings.environment.cwd,
                this._settings.relpathFailoverServiceHome));

        const myself = this;
        function computeServiceInstalled(x: DecodedServiceInstalled): boolean {
            switch (x) {
                case DecodedServiceInstalled.True: return true;
                case DecodedServiceInstalled.False: return false;
                case DecodedServiceInstalled.Neither:
                default:
                    return !myself._settings.environment.fileExists(
                        NodePath.join(
                            serviceHome,
                            myself._settings.filenameUserHostService));
            }
        }
        const serviceInstalled = computeServiceInstalled(
            decodeRawServiceInstalled(this._settings.environment.getVar(this._settings.envvarnameServiceInstalled)));

        return {
            serviceHome,
            serviceInstalled
        };
    }
}
