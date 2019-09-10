// tslint:disable: variable-name

import { Observable } from 'rxjs';
import {
    LocalProcessInformation, JobStatusChangedEventArgs, JobCompletedEventArgs,
    InstallProcessParameters, JobData, StartJobParameters, StopJobParameters,
    PauseJobParameters, ResumeJobParameters
} from './upstream-contract';

export interface IRobotAgentProxy {
    readonly RobotStatusChanged: Observable<RobotStatusChangedEventArgs>;
    readonly ProcessListUpdated: Observable<ProcessListUpdatedArgs>;
    readonly JobStatusChanged: Observable<JobStatusChangedEventArgs>;
    readonly JobCompleted: Observable<JobCompletedEventArgs>;

    RefreshStatus(parameters: RefreshStatusParameters): Promise<void>;
    InstallProcess(parameters: InstallProcessParameters): Promise<void>;
    StartJob(parameters: StartJobParameters): Promise<JobData>;
    StopJob(parameters: StopJobParameters): Promise<void>;
    PauseJob(parameters: PauseJobParameters): Promise<void>;
    ResumeJob(parameters: ResumeJobParameters): Promise<void>;
}


// export interface IRobotAgentProxy {
//     readonly RobotStatusChanged: Observable<DownstreamContract.RobotStatusChangedEventArgs>;
//     readonly ProcessListUpdated: Observable<DownstreamContract.ProcessListUpdatedArgs>;
//     readonly JobStatusChanged: Observable<UpstreamContract.JobStatusChangedEventArgs>;
//     readonly JobCompleted: Observable<UpstreamContract.JobCompletedEventArgs>;

//     RefreshStatus(parameters: DownstreamContract.RefreshStatusParameters): Promise<void>;
//     InstallProcess(parameters: UpstreamContract.InstallProcessParameters): Promise<void>;
//     StartJob(parameters: UpstreamContract.StartJobParameters): Promise<UpstreamContract.JobData>;
//     StopJob(parameters: UpstreamContract.StopJobParameters): Promise<void>;
//     PauseJob(parameters: UpstreamContract.PauseJobParameters): Promise<void>;
//     ResumeJob(parameters: UpstreamContract.ResumeJobParameters): Promise<void>;
// }

export interface RobotStatusChangedEventArgs {
    readonly Status: RobotStatus;
    readonly LogInError: string | null;
}

export interface ProcessListUpdatedArgs {
    readonly Processes: ReadonlyArray<LocalProcessInformation>;
}

export enum RobotStatus {
    Offline,
    ServiceUnavailable,
    ConnectionFailed,
    Connecting,
    LoggingIn,
    LogInFailed,
    Connected,
}

export interface RefreshStatusParameters {
    ForceProcessListUpdate: boolean;
}
