import * as DownstreamContract from './downstream-contract';
import * as UpstreamContract from './upstream-contract';
import { Observable, Subject, Observer } from 'rxjs';
import { IRobotProxy, RobotProxy } from './robot-proxy';
import { Trace, Message } from '@uipath/ipc';
import '@uipath-ipc';
import { RobotConfig } from '.';
import { spawn } from 'child_process';

/* @internal */
export class RobotAgentProxy implements DownstreamContract.IRobotAgentProxy {
    private static createSubject<T>(): Observer<T> & Observable<T> { return new Subject<T>(); }

    private readonly _proxy: IRobotProxy;
    private _orchestratorStatus: UpstreamContract.OrchestratorStatus = UpstreamContract.OrchestratorStatus.Offline;
    private _initialized = false;
    private _disposed = false;

    private readonly _robotStatusChanged = RobotAgentProxy.createSubject<DownstreamContract.RobotStatusChangedEventArgs>();
    private readonly _processListUpdated = RobotAgentProxy.createSubject<DownstreamContract.ProcessListUpdatedArgs>();
    private readonly _jobStatusChanged = RobotAgentProxy.createSubject<UpstreamContract.JobStatusChangedEventArgs>();
    private readonly _jobCompleted = RobotAgentProxy.createSubject<UpstreamContract.JobCompletedEventArgs>();

    constructor();
    constructor(proxy: IRobotProxy);
    constructor(proxy?: IRobotProxy) {
        this._proxy = proxy || new RobotProxy();

        this._proxy.ServiceUnavailable.subscribe(_ => this.raiseRobotStatusChanged(DownstreamContract.RobotStatus.ServiceUnavailable));
        this._proxy.OrchestratorStatusChanged.subscribe(this.onOrchestratorStatusChanged.bind(this));

        this._proxy.JobCompleted.subscribe(args => {
            this._jobCompleted.next(args);
        });

        this._proxy.JobStatusChanged.subscribe(args => {
            this._jobStatusChanged.next(args);
        });

        this._proxy.ProcessListChanged.subscribe(async _ => {
            try {
                await this.refreshStatusCore();
            } catch (error) {
                Trace.log(error);
            }
        });
    }

    // #region " Implementation of IRobotAgentProxy "

    public get RobotStatusChanged(): Observable<DownstreamContract.RobotStatusChangedEventArgs> { return this._robotStatusChanged; }
    public get ProcessListUpdated(): Observable<DownstreamContract.ProcessListUpdatedArgs> { return this._processListUpdated; }
    public get JobStatusChanged(): Observable<UpstreamContract.JobStatusChangedEventArgs> { return this._jobStatusChanged; }
    public get JobCompleted(): Observable<UpstreamContract.JobCompletedEventArgs> { return this._jobCompleted; }

    public RefreshStatus(parameters: DownstreamContract.RefreshStatusParameters): void {
        this.assertNotClosed();
        if (!this._initialized) {
            this.connectToService().traceError();
            this._initialized = true;
            return;
        }
        this.refreshStatusCore(new UpstreamContract.GetProcessesParameters({
            ForceRefresh: parameters.ForceProcessListUpdate,
            AutoInstall: parameters.ForceProcessListUpdate
        }));
    }

    private async connectToService(): Promise<void> {
        while (true) {
            try {
                await this._proxy.GetUserStatus(new Message<void>());
                return;
            } catch (error) {
                Trace.log(`error instanceof Error === ${error instanceof Error}`);

                Trace.log(`RobotAgentProxy.connectToService: calling this._proxy.GetUserStatus() failed (will retry in 3 seconds)\r\nError: "${error}"`);
                await Promise.delay(3000);
            }
        }
    }

    public async InstallProcess(parameters: UpstreamContract.InstallProcessParameters): Promise<void> {
        this.assertNotClosed();
        await this._proxy.InstallProcess(parameters);
    }
    public async StartJob(parameters: UpstreamContract.StartJobParameters): Promise<UpstreamContract.JobData> {
        this.assertNotClosed();
        return await this._proxy.StartAgentJob(parameters);
    }
    public async StopJob(parameters: UpstreamContract.StopJobParameters): Promise<void> {
        this.assertNotClosed();
        await this._proxy.StopJob(parameters);
    }
    public async PauseJob(parameters: UpstreamContract.PauseJobParameters): Promise<void> {
        this.assertNotClosed();
        await this._proxy.PauseJob(parameters);
    }
    public async ResumeJob(parameters: UpstreamContract.ResumeJobParameters): Promise<void> {
        this.assertNotClosed();
        this._proxy.ResumeJob(parameters);
    }
    public async OpenOrchestratorSettings(): Promise<void> {
        this.assertNotClosed();
        spawn(
            RobotConfig.data.oldAgentFilePath,
            ['settings'],
            {
                detached: true,
                shell: false
            }
        ).unref();
    }

    public async CloseAsync(): Promise<void> {
        this._disposed = true;
        await this._proxy.CloseAsync();
    }

    // #endregion

    // #region " Internals "

    private assertNotClosed(): void {
        if (this._disposed) {
            throw new Error('Cannot access a closed RobotAgentProxy.');
        }
    }

    private async refreshStatusCore(parameters?: UpstreamContract.GetProcessesParameters): Promise<void> {
        try {
            await this.ensureLogin();
            this.updateProcesses(parameters);
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
    private async updateProcesses(parameters?: UpstreamContract.GetProcessesParameters): Promise<void> {
        try {
            // tslint:disable-next-line: variable-name
            const Processes = await this._proxy.GetAvailableProcesses(parameters || new UpstreamContract.GetProcessesParameters());
            this._processListUpdated.next({ Processes });
        } catch (error) {
            Trace.log(error);
            this._processListUpdated.next({ Processes: [] });
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

        const triggerAutomaticActions = args.OrchestratorStatus === UpstreamContract.OrchestratorStatus.Connected;
        this.refreshStatusCore(new UpstreamContract.GetProcessesParameters({
            AutoStart: triggerAutomaticActions,
            AutoInstall: triggerAutomaticActions
        }));
    }

    // #endregion
}
