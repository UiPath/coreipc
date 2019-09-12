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
    ResumeJobParameters
} from './upstream-contract';
import { RobotConfig, IRobotEnvironment } from './robot-config';

const RobotProxyConstructor: new () => IRobotAgentProxy = RobotAgentProxy;

export {
    RobotProxyConstructor,
    IRobotAgentProxy,

    RobotConfig,
    IRobotEnvironment,

    // Downstream contract DTOs
    RobotStatusChangedEventArgs,
    ProcessListUpdatedArgs,
    RobotStatus,
    RefreshStatusParameters,

    // Common contract DTOs
    LocalProcessInformation,
    StartJobParameters,
    StopJobParameters,
    InstallProcessParameters,
    PauseJobParameters,
    ResumeJobParameters
};
