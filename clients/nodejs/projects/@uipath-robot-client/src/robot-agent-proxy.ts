import * as DownstreamContract from './downstream-contract';
import * as UpstreamContract from './upstream-contract';
import { Observable, Subject, Observer } from 'rxjs';
import { IRobotProxy, RobotProxy } from './robot-proxy';
import { Trace } from '@uipath/ipc';

/* @internal */
export class RobotAgentProxy implements DownstreamContract.IRobotAgentProxy {
    private static createSubject<T>(): Observer<T> & Observable<T> { return new Subject<T>(); }

    private readonly _proxy: IRobotProxy;
    private _orchestratorStatus: UpstreamContract.OrchestratorStatus = UpstreamContract.OrchestratorStatus.Offline;
    private _initialized = false;

    private readonly _robotStatusChanged = RobotAgentProxy.createSubject<DownstreamContract.RobotStatusChangedEventArgs>();
    private readonly _processListUpdated = RobotAgentProxy.createSubject<DownstreamContract.ProcessListUpdatedArgs>();
    private readonly _jobStatusChanged = RobotAgentProxy.createSubject<UpstreamContract.JobStatusChangedEventArgs>();
    private readonly _jobCompleted = RobotAgentProxy.createSubject<UpstreamContract.JobCompletedEventArgs>();

    constructor() {
        this._proxy = new RobotProxy();

        this._proxy.ServiceUnavailable.subscribe(_ => this.raiseRobotStatusChanged(DownstreamContract.RobotStatus.ServiceUnavailable));
        this._proxy.OrchestratorStatusChanged.subscribe(this.onOrchestratorStatusChanged.bind(this));
        this._proxy.LogInSessionExpired.subscribe(this.onLogInSessionExpired.bind(this));
        this._proxy.JobStatusChanged.subscribe(this._jobStatusChanged);
        this._proxy.JobCompleted.subscribe(this._jobCompleted);
    }

    // #region " Implementation of IRobotAgentProxy "

    public get RobotStatusChanged(): Observable<DownstreamContract.RobotStatusChangedEventArgs> { return this._robotStatusChanged; }
    public get ProcessListUpdated(): Observable<DownstreamContract.ProcessListUpdatedArgs> { return this._processListUpdated; }
    public get JobStatusChanged(): Observable<UpstreamContract.JobStatusChangedEventArgs> { return this._jobStatusChanged; }
    public get JobCompleted(): Observable<UpstreamContract.JobCompletedEventArgs> { return this._jobCompleted; }

    public async RefreshStatus(parameters: DownstreamContract.RefreshStatusParameters): Promise<void> {
        if (!this._initialized) {
            this._initialized = true;
            return;
        }
        this.refreshStatusCore(parameters.ForceProcessListUpdate);
    }
    public async InstallProcess(parameters: UpstreamContract.InstallProcessParameters): Promise<void> { await this._proxy.InstallProcess(parameters); }
    public StartJob(parameters: UpstreamContract.StartJobParameters): Promise<UpstreamContract.JobData> { return this._proxy.StartAgentJob(parameters); }
    public StopJob(parameters: UpstreamContract.StopJobParameters): Promise<void> { return this._proxy.StopJob(parameters); }
    public PauseJob(parameters: UpstreamContract.PauseJobParameters): Promise<void> { return this._proxy.PauseJob(parameters); }
    public ResumeJob(parameters: UpstreamContract.ResumeJobParameters): Promise<void> { return this._proxy.ResumeJob(parameters); }

    // #endregion

    private async refreshStatusCore(force: boolean = false) {
        try {
            await this.ensureLogin();
            this.updateProcesses();
        } catch (error) {
            Trace.log(error);
        }
    }
    private async ensureLogin(): Promise<void> {
        if (this._orchestratorStatus === UpstreamContract.OrchestratorStatus.Connected) {
            try {
                await this._proxy.LogInUser();
                this.raiseLicenseStatus(DownstreamContract.RobotStatus.Connected);
            } catch (error) {
                const errorMessage = error instanceof Error ? error.message : `${error}`;
                this.raiseLicenseStatus(DownstreamContract.RobotStatus.LogInFailed, errorMessage);
            }
        }
    }
    private async updateProcesses(force: boolean = false): Promise<void> {
        try {
            // tslint:disable-next-line: variable-name
            const Processes = await this._proxy.GetAvailableProcesses(new UpstreamContract.GetProcessesParameters({ ForceRefresh: force }));
            this._processListUpdated.next({ Processes });
        } catch (error) {
            Trace.log(error);
        }
    }

    private raiseLicenseStatus(status: DownstreamContract.RobotStatus, logInError: string | null = null) {
        if (this._orchestratorStatus !== UpstreamContract.OrchestratorStatus.Offline) {
            this.raiseRobotStatusChanged(status, logInError);
        }
    }
    private raiseRobotStatusChanged(status: DownstreamContract.RobotStatus, logInError: string | null = null): void {
        this._robotStatusChanged.next({
            Status: status,
            LogInError: logInError
        });
    }

    private onOrchestratorStatusChanged(args: UpstreamContract.OrchestratorStatusChangedEventArgs): void {
        function getStatus(): DownstreamContract.RobotStatus {
            switch (args.OrchestratorStatus) {
                case UpstreamContract.OrchestratorStatus.Offline: return DownstreamContract.RobotStatus.Offline;
                case UpstreamContract.OrchestratorStatus.Connecting: return DownstreamContract.RobotStatus.Connecting;
                case UpstreamContract.OrchestratorStatus.ConnectionFailed: return DownstreamContract.RobotStatus.ConnectionFailed;
                case UpstreamContract.OrchestratorStatus.Connected: return DownstreamContract.RobotStatus.LoggingIn;
            }
            throw new Error('ArgumentOutOfRange');
        }

        this.raiseRobotStatusChanged(getStatus());
        const oldStatus = this._orchestratorStatus;
        this._orchestratorStatus = args.OrchestratorStatus;

        if (oldStatus === UpstreamContract.OrchestratorStatus.ConnectionFailed && args.OrchestratorStatus === UpstreamContract.OrchestratorStatus.Connected) {
            return;
        }
        this.refreshStatusCore();
    }
    private onLogInSessionExpired(): void {
        this.raiseRobotStatusChanged(DownstreamContract.RobotStatus.LoggingIn);
        this.updateProcesses();
    }
}
