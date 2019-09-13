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
    readonly ProcessData: ProcessData | null;
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

    constructor(options?: { ForceRefresh: boolean }) {
        super();
        if (options) {
            this.ForceRefresh = options.ForceRefresh;
        } else {
            this.ForceRefresh = false;
        }
    }
}

export interface ProcessData {
    readonly Key: string;
    readonly Name: string;
    readonly Version: string;
    readonly FolderName: string;
}
export interface ProcessSettings {
    readonly AutoStart: boolean;
    readonly AutoInstall: boolean;
}
export interface LocalProcessInformation {
    readonly Process: ProcessData;
    readonly Settings: ProcessSettings;
    readonly Installed: boolean;
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

export class IAgentOperations {
    public SubscribeToEvents(message: Message<void>): Promise<boolean> { throw null; }

    // Jobs
    @__hasCancellationToken__
    public StartAgentJob(parameters: StartJobParameters, ct?: CancellationToken): Promise<JobData> { throw null; }

    @__hasCancellationToken__
    public StopJob(parameters: StopJobParameters, ct?: CancellationToken): Promise<void> { throw null; }

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
}

export interface IAgentEvents {
    OnJobCompleted(args: JobCompletedEventArgs): Promise<void>;
    OnJobStatusUpdated(args: JobStatusChangedEventArgs): Promise<void>;
    OnOrchestratorStatusChanged(args: OrchestratorStatusChangedEventArgs): Promise<void>;
    OnLogInSessionExpired(message: Message<void>): Promise<void>;
}
