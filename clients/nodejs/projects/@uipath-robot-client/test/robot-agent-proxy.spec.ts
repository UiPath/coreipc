// tslint:disable: variable-name
import { RobotAgentProxy } from '../src/robot-agent-proxy';
import { IRobotProxy } from '../src/robot-proxy';
import { Observable } from 'rxjs';
import {
    JobCompletedEventArgs,
    JobStatusChangedEventArgs,
    OrchestratorStatusChangedEventArgs,
    PauseJobParameters,
    GetProcessesParameters,
    ResumeJobParameters,
    StartJobParameters,
    StopJobParameters,
    UserStatus,
    LocalProcessInformation,
    InstallProcessParameters,
    LogInParameters
} from '../src/upstream-contract';
import { CancellationToken, Message } from '@uipath/ipc';

describe(`RobotAgentProxy`, () => {

    // test(``, () => {
    //     class Mock implements IRobotProxy {
    //         public JobCompleted: Observable<JobCompletedEventArgs>;
    //         public JobStatusChanged: Observable<JobStatusChangedEventArgs>;
    //         public OrchestratorStatusChanged: Observable<OrchestratorStatusChangedEventArgs>;
    //         public ServiceUnavailable: Observable<void>;
    //         public LogInSessionExpired: Observable<void>;

    //         public StartEvents(): void {
    //             throw new Error('Method not implemented.');
    //         }
    //         public StartAgentJob(parameters: StartJobParameters, ct?: CancellationToken): Promise<import('../src/upstream-contract').JobData> {
    //             throw new Error('Method not implemented.');
    //         }
    //         public StopJob(parameters: StopJobParameters, ct?: CancellationToken): Promise<void> {
    //             throw new Error('Method not implemented.');
    //         }
    //         public PauseJob(parameters: PauseJobParameters, ct?: CancellationToken): Promise<void> {
    //             throw new Error('Method not implemented.');
    //         }
    //         public SubscribeToEvents(message: Message<void>): Promise<boolean> {
    //             throw new Error('Method not implemented.');
    //         }
    //         public ResumeJob(parameters: ResumeJobParameters, ct?: CancellationToken): Promise<void> {
    //             throw new Error('Method not implemented.');
    //         }
    //         public GetAvailableProcesses(parameters?: GetProcessesParameters, ct?: CancellationToken): Promise<readonly LocalProcessInformation[]> {
    //             throw new Error('Method not implemented.');
    //         }
    //         public InstallProcess(parameters: InstallProcessParameters, ct?: CancellationToken): Promise<boolean> {
    //             throw new Error('Method not implemented.');
    //         }

    //         public LogInUser(ct?: CancellationToken): Promise<UserStatus>;
    //         public LogInUser(parameters: LogInParameters, ct?: CancellationToken): Promise<UserStatus>;
    //         public LogInUser(parameters?: any, ct?: any): Promise<any> {
    //             throw new Error('Method not implemented.');
    //         }
    //     }
    //     const mock = new Mock();
    //     const proxy = new RobotAgentProxy(mock);
    // });

});
