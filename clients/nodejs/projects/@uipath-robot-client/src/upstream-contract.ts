// tslint:disable: no-shadowed-variable
// tslint:disable: variable-name

import { __hasCancellationToken__, Message, CancellationToken, IpcError } from '@uipath/ipc';

export class JobCompletedEventArgs extends Message<void> {
    constructor(
        public readonly Job: JobData,
        public readonly Status: CompletedStatus,
        public readonly Exception: IpcError
    ) {
        super();
    }
}

export class JobStatusChangedEventArgs extends Message<void> {
    constructor(
        public readonly Job: JobData,
        public readonly Status: JobStatus,
        public readonly StatusText: string,
        public readonly ProjectSettings: ProjectSettings | null
    ) {
        super();
    }
}

export interface JobData {
    readonly Process: ProcessData | null;
    readonly DisplayName: string;
    readonly Identifier: string;
}
export enum CompletedStatus {
    Succeeded,
    Stopped,
    Failed,
}
export enum JobStatus {
    Running,
    Stopping,
    Paused
}

export class GetProcessesParameters extends Message<void> {
    public ForceRefresh: boolean;
    public AutoStart: boolean;
    public AutoInstall: boolean;

    constructor(options?: Partial<{ ForceRefresh: boolean, AutoStart: boolean, AutoInstall: boolean }>) {
        super();
        if (options) {
            this.ForceRefresh = options.ForceRefresh || false;
            this.AutoStart = options.AutoStart || false;
            this.AutoInstall = options.AutoInstall || false;
        } else {
            this.ForceRefresh = false;
            this.AutoStart = false;
            this.AutoInstall = false;
        }
    }
}

export interface ProcessData {
    readonly Key: string;
    readonly Name: string;
    readonly Version: string;
    readonly FolderName: string;
    readonly FolderPath: string;
}
export interface LocalProcessInformation {
    readonly Process: ProcessData;
    readonly InstallationState: ProcessInstallationState;
}
export enum ProcessInstallationState {
    NotInstalled,
    Installing,
    Installed
}
export class ProjectSettings {
    constructor(
        public readonly Pausable: boolean,
        public readonly Background: boolean
    ) { }
}

export enum OrchestratorStatus {
    Offline,
    Connecting,
    Connected,
    ConnectionFailed,
}

export class OrchestratorStatusChangedEventArgs extends Message<void> {
    constructor(public readonly OrchestratorStatus: OrchestratorStatus) { super(); }
}

export class InstallProcessParameters extends Message<void> {
    constructor(
        public readonly ProcessKey: string
    ) { super(); }
}
export class StartJobParameters extends Message<void> {
    constructor(
        public readonly ProcessKey: string
    ) { super(); }
}
export class StopJobParameters extends Message<void> {
    constructor(
        public readonly JobIdentifier: string
    ) { super(); }
}
export class PauseJobParameters extends Message<void> {
    constructor(
        public readonly JobIdentifier: string
    ) { super(); }
}
export class ResumeJobParameters extends Message<void> {
    constructor(
        public readonly JobIdentifier: string
    ) { super(); }
}
export class LogInParameters extends Message<void> {
    constructor(
        public readonly ClientProcessId: number
    ) { super(); }
}
export class GetProjectDetailsByKeyParameters extends Message<void> {
    constructor(
        public readonly ProcessKey: string
    ) { super(); }
}
export interface UserStatus {
    // Unsupported --->
    OrchestratorStatus: OrchestratorStatus;
    Robot: RobotData | null;
    // <---
}
export interface RobotData {
    RobotType: RobotType;
    IsOrchestratorCommunityEdition: boolean;
    HasLicense: boolean;
    ServerExecutionSettings: SettingsDictionary;
}
export interface SettingsDictionary {
    [key: string]: any;
}
export enum RobotType {
    NonProduction = 0,
    Attended = 1,
    Unattended = 2,
    Development = 3
}

export interface ProjectDetails {
    readonly Settings: ProjectSettings;
    readonly Description: string;
    readonly InputArguments: readonly InputArgument[];
}
export interface InputArgument {
    readonly Name: string;
    readonly Type: string;
    readonly IsRequired: boolean;
    readonly HasDefault: boolean;
}

export class ProcessListChangedEventArgs extends Message<void> {
    constructor() { super(); }
}

export interface IAgentOperations {
    SubscribeToEvents(message: Message<void>): Promise<boolean>;

    // Jobs
    StartAgentJob(parameters: StartJobParameters, ct?: CancellationToken): Promise<JobData>;
    StopJob(parameters: StopJobParameters, ct?: CancellationToken): Promise<boolean>;
    PauseJob(parameters: PauseJobParameters, ct?: CancellationToken): Promise<void>;
    ResumeJob(parameters: ResumeJobParameters, ct?: CancellationToken): Promise<void>;

    // Processes
    GetAvailableProcesses(parameters?: GetProcessesParameters, ct?: CancellationToken): Promise<ReadonlyArray<LocalProcessInformation>>;
    InstallProcess(parameters: InstallProcessParameters, ct?: CancellationToken): Promise<boolean>;

    // Orchestrator
    LogInUser(parameters: LogInParameters, ct?: CancellationToken): Promise<UserStatus>;

    // IClientOperations
    GetUserStatus(message: Message<void>, ct?: CancellationToken): Promise<UserStatus>;
    GetProjectDetailsByKey(parameters: GetProjectDetailsByKeyParameters, ct?: CancellationToken): Promise<ProjectDetails>;
}

export class AgentOperations implements IAgentOperations {
    public SubscribeToEvents(message: Message<void>): Promise<boolean> { throw null; }

    // Jobs
    @__hasCancellationToken__
    public StartAgentJob(parameters: StartJobParameters, ct?: CancellationToken): Promise<JobData> { throw null; }

    @__hasCancellationToken__
    public StopJob(parameters: StopJobParameters, ct?: CancellationToken): Promise<boolean> { throw null; }

    @__hasCancellationToken__
    public PauseJob(parameters: PauseJobParameters, ct?: CancellationToken): Promise<void> { throw null; }

    @__hasCancellationToken__
    public ResumeJob(parameters: ResumeJobParameters, ct?: CancellationToken): Promise<void> { throw null; }

    // Processes
    @__hasCancellationToken__
    public GetAvailableProcesses(parameters?: GetProcessesParameters, ct?: CancellationToken): Promise<ReadonlyArray<LocalProcessInformation>> { throw null; }

    @__hasCancellationToken__
    public InstallProcess(parameters: InstallProcessParameters, ct?: CancellationToken): Promise<boolean> { throw null; }

    // Orchestrator
    @__hasCancellationToken__
    public LogInUser(parameters: LogInParameters, ct?: CancellationToken): Promise<UserStatus> { throw null; }

    // IClientOperations
    @__hasCancellationToken__
    public GetUserStatus(message: Message<void>, ct?: CancellationToken): Promise<UserStatus> { throw null; }

    @__hasCancellationToken__
    public GetProjectDetailsByKey(parameters: GetProjectDetailsByKeyParameters, ct?: CancellationToken): Promise<ProjectDetails> { throw null; }
}

export interface IAgentEvents {
    OnJobCompleted(args: JobCompletedEventArgs): Promise<void>;
    OnJobStatusUpdated(args: JobStatusChangedEventArgs): Promise<void>;
    OnOrchestratorStatusChanged(args: OrchestratorStatusChangedEventArgs): Promise<void>;
    OnProcessListChanged(args: ProcessListChangedEventArgs): Promise<void>;
}
