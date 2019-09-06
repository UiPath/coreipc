// tslint:disable: variable-name
// tslint:disable: no-namespace
import { CancellationToken, Message, __hasCancellationToken__, __returns__, IpcError } from '@uipath/ipc';
export { };

export namespace Contract {

    export class IAgentOperations {

        public SubscribeToEvents(message: Message<void>): Promise<boolean> { throw null; }

        @__hasCancellationToken__
        public GetAvailableProcesses(parameters?: GetProcessesParameters, ct?: CancellationToken): Promise<ReadonlyArray<LocalProcessInformation>> { throw null; }

    }

    export interface IAgentEvents {
        OnJobCompleted(args: JobCompletedEventArgs): Promise<boolean>;
    }

    export class JobCompletedEventArgs extends Message<void> {
        constructor(
            public readonly Job: JobData,
            public readonly Status: CompletedStatus,
            public readonly Exception: IpcError
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

    export class GetProcessesParameters extends Message<void> {
        constructor(
            public SkipCache: boolean = false
        ) {
            super();
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

}
