// tslint:disable: max-line-length
import { IpcClient, Message, PipeClientStream, CancellationToken, TimeSpan, PromisePal, Trace } from '@uipath/ipc';
import { Subject, Observable, Observer } from 'rxjs';

import * as UpstreamContract from './upstream-contract';
import { Utils } from './utils';
import { RobotClientSettings } from './robot-client-settings';
import { RobotEnvironmentStore } from './robot-environment';

export interface IRobotProxy {
    readonly JobCompleted: Observable<UpstreamContract.JobCompletedEventArgs>;
    readonly JobStatusChanged: Observable<UpstreamContract.JobStatusChangedEventArgs>;
    readonly OrchestratorStatusChanged: Observable<UpstreamContract.OrchestratorStatusChangedEventArgs>;
    readonly ServiceUnavailable: Observable<void>;
    readonly LogInSessionExpired: Observable<void>;

    StartEvents(): void;

    StartAgentJob(parameters: UpstreamContract.StartJobParameters, ct?: CancellationToken): Promise<UpstreamContract.JobData>;
    StopJob(parameters: UpstreamContract.StopJobParameters, ct?: CancellationToken): Promise<void>;
    PauseJob(parameters: UpstreamContract.PauseJobParameters, ct?: CancellationToken): Promise<void>;
    SubscribeToEvents(message: Message<void>): Promise<boolean>;
    ResumeJob(parameters: UpstreamContract.ResumeJobParameters, ct?: CancellationToken | undefined): Promise<void>;
    GetAvailableProcesses(parameters?: UpstreamContract.GetProcessesParameters | undefined, ct?: CancellationToken | undefined): Promise<readonly UpstreamContract.LocalProcessInformation[]>;
    InstallProcess(parameters: UpstreamContract.InstallProcessParameters, ct?: CancellationToken | undefined): Promise<boolean>;

    LogInUser(ct?: CancellationToken | undefined): Promise<UpstreamContract.UserStatus>;
    LogInUser(parameters: UpstreamContract.LogInParameters, ct?: CancellationToken | undefined): Promise<UpstreamContract.UserStatus>;
}

/* @internal */
export class RobotProxy extends UpstreamContract.IAgentOperations {
    private static readonly _data = (() => {
        try {
            Trace.log(`Calling RobotEnvironmentStore.initialize()...`);
            RobotEnvironmentStore.initialize();
            return RobotEnvironmentStore.data;
        } finally {
            Trace.log(`...DONE!`);
        }
    })();

    private static createSubject<T>(): Observer<T> & Observable<T> {
        return new Subject<T>();
    }

    private readonly _jobCompleted = RobotProxy.createSubject<UpstreamContract.JobCompletedEventArgs>();
    private readonly _jobStatusChanged = RobotProxy.createSubject<UpstreamContract.JobStatusChangedEventArgs>();
    private readonly _orchestratorStatusChanged = RobotProxy.createSubject<UpstreamContract.OrchestratorStatusChangedEventArgs>();
    private readonly _serviceUnavailable = RobotProxy.createSubject<void>();
    private readonly _logInSessionExpired = RobotProxy.createSubject<void>();

    private readonly _ipcClient: IpcClient<UpstreamContract.IAgentOperations>;
    private get channel(): UpstreamContract.IAgentOperations { return this._ipcClient.proxy; }

    constructor() {
        super();

        Trace.log(`Calling Utils.getPipeName(serviceInstalled: ${RobotProxy._data.serviceInstalled})...`);
        const pipeName = Utils.getPipeName(RobotProxy._data.serviceInstalled);
        Trace.log(`===> ${pipeName}`);

        class AgentEvents implements UpstreamContract.IAgentEvents {
            constructor(private readonly _owner: RobotProxy) { }
            public async OnJobCompleted(args: UpstreamContract.JobCompletedEventArgs): Promise<void> {
                this._owner._jobCompleted.next(args);
            }
            public async OnJobStatusUpdated(args: UpstreamContract.JobStatusChangedEventArgs): Promise<void> {
                this._owner._jobStatusChanged.next(args);
            }
            public async OnOrchestratorStatusChanged(args: UpstreamContract.OrchestratorStatusChangedEventArgs): Promise<void> {
                this._owner._orchestratorStatusChanged.next(args);
            }
            public async OnLogInSessionExpired(message: Message<void>): Promise<void> {
                this._owner._logInSessionExpired.next(undefined);
            }
        }

        this._ipcClient = new IpcClient(
            pipeName,
            UpstreamContract.IAgentOperations,
            config => {
                config.callbackService = new AgentEvents(this);

                config.setConnectionFactory(async (connect, ct) => {
                    Utils.spawnService();
                    return await this.spinWaitForNamedPipeAsync(connect, ct);
                });

                config.setBeforeCall(async (methodName, newConnection, ct) => {
                    if (newConnection) {
                        try {
                            Trace.log('calling SubscribeToEvents');
                            await this._ipcClient.proxy.SubscribeToEvents(new Message<void>());
                            Trace.log('DONE (SubscribeToEvents)!');
                        } catch (error) {
                            Trace.log(error);
                        }
                    }
                });
            }
        );
    }

    private async spinWaitForNamedPipeAsync(connect: () => Promise<PipeClientStream>, ct: CancellationToken): Promise<PipeClientStream> {
        while (true) {
            try {
                ct.throwIfCancellationRequested();
            } catch (error) {
                this._serviceUnavailable.next(undefined);
                throw error;
            }

            try {
                const result = await connect();
                return result;
            } catch (error) {
                const errorText = error instanceof Error ? error.message : `${error}`;
            }

            const pause = TimeSpan.fromMilliseconds(300);
            await PromisePal.delay(pause);
        }
    }

    // #region " Implementation of IRobotProxy "

    public get JobCompleted(): Observable<UpstreamContract.JobCompletedEventArgs> { return this._jobCompleted; }
    public get JobStatusChanged(): Observable<UpstreamContract.JobStatusChangedEventArgs> { return this._jobStatusChanged; }
    public get OrchestratorStatusChanged(): Observable<UpstreamContract.OrchestratorStatusChangedEventArgs> { return this._orchestratorStatusChanged; }
    public get ServiceUnavailable(): Observable<void> { return this._serviceUnavailable; }
    public get LogInSessionExpired(): Observable<void> { return this._logInSessionExpired; }

    public StartEvents(): void {
        (async () => {
            try {
                Trace.log('calling GetUserStatus...');
                await this.channel.GetUserStatus(new Message<void>());
                Trace.log('....DONE!');
            } catch (error) {
                Trace.log(error);
            }
        })();
    }

    public StartAgentJob(parameters: UpstreamContract.StartJobParameters, ct?: CancellationToken): Promise<UpstreamContract.JobData> {
        return this.channel.StartAgentJob(parameters, ct);
    }
    public StopJob(parameters: UpstreamContract.StopJobParameters, ct?: CancellationToken): Promise<void> {
        return this.channel.StopJob(parameters, ct);
    }
    public PauseJob(parameters: UpstreamContract.PauseJobParameters, ct?: CancellationToken): Promise<void> {
        return this.channel.PauseJob(parameters, ct);
    }
    public SubscribeToEvents(message: Message<void>): Promise<boolean> {
        return this.channel.SubscribeToEvents(message);
    }
    public ResumeJob(parameters: UpstreamContract.ResumeJobParameters, ct?: CancellationToken | undefined): Promise<void> {
        return this.channel.ResumeJob(parameters, ct);
    }
    public GetAvailableProcesses(parameters?: UpstreamContract.GetProcessesParameters | undefined, ct?: CancellationToken | undefined): Promise<readonly UpstreamContract.LocalProcessInformation[]> {
        return this.channel.GetAvailableProcesses(parameters, ct);
    }
    public InstallProcess(parameters: UpstreamContract.InstallProcessParameters, ct?: CancellationToken | undefined): Promise<boolean> {
        parameters.RequestTimeout = RobotClientSettings.installPackageRequestTimeout;
        return this.channel.InstallProcess(parameters, ct);
    }

    public LogInUser(ct?: CancellationToken): Promise<UpstreamContract.UserStatus>;
    public LogInUser(parameters: UpstreamContract.LogInParameters, ct?: CancellationToken): Promise<UpstreamContract.UserStatus>;
    public LogInUser(maybeParametersOrCt: UpstreamContract.LogInParameters | CancellationToken | undefined, maybeCt?: CancellationToken | undefined): Promise<UpstreamContract.UserStatus> {
        let parameters: UpstreamContract.LogInParameters;
        let ct: CancellationToken;

        if (maybeParametersOrCt === undefined && maybeCt === undefined) {
            parameters = new UpstreamContract.LogInParameters(process.pid);
            ct = CancellationToken.none;
        } else if (maybeParametersOrCt instanceof CancellationToken && maybeCt === undefined) {
            parameters = new UpstreamContract.LogInParameters(process.pid);
            ct = maybeParametersOrCt;
        } else if (maybeParametersOrCt instanceof UpstreamContract.LogInParameters && maybeCt === undefined) {
            parameters = maybeParametersOrCt;
            ct = CancellationToken.none;
        } else if (maybeParametersOrCt instanceof UpstreamContract.LogInParameters && maybeCt instanceof CancellationToken) {
            parameters = maybeParametersOrCt;
            ct = maybeCt;
        } else {
            throw new Error('Overload not supported.');
        }

        return this.channel.LogInUser(parameters, ct);
    }

    // #endregion }
}
