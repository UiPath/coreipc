// tslint:disable: variable-name
import {
    RobotAgentProxy
} from './robot-agent-proxy';

import {
    IRobotAgentProxy,
    RobotStatusChangedEventArgs,
    ProcessListUpdatedArgs,
    RobotStatus,
    RefreshStatusParameters,
} from './downstream-contract';

import {
    LocalProcessInformation,
    StartJobParameters,
    StopJobParameters,
    InstallProcessParameters,
    PauseJobParameters,
    ResumeJobParameters,
    ProcessData,
    ProcessSettings,
    JobStatusChangedEventArgs,
    JobCompletedEventArgs,
    JobData,
    JobStatus,
    ProjectSettings,
    CompletedStatus
} from './upstream-contract';
import { RobotConfig, IRobotEnvironment } from './robot-config';
import { Message, IpcError } from '@uipath/ipc';

const RobotProxyConstructor: new () => IRobotAgentProxy = RobotAgentProxy;

export { default } from '@uipath/ipc';
export {
    RobotProxyConstructor,
    IRobotAgentProxy,

    RobotConfig,
    IRobotEnvironment,

    // Ipc DTOs
    Message,
    IpcError,

    // Downstream contract DTOs
    RobotStatusChangedEventArgs,
    ProcessListUpdatedArgs,
    RobotStatus,
    RefreshStatusParameters,

    // Common contract DTOs
    LocalProcessInformation,
    ProcessData,
    ProcessSettings,
    StartJobParameters,
    StopJobParameters,
    InstallProcessParameters,
    PauseJobParameters,
    ResumeJobParameters,

    JobStatusChangedEventArgs,
    JobCompletedEventArgs,
    JobData,
    JobStatus,
    ProjectSettings,
    CompletedStatus
};
