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
    StopJobParameters
} from './upstream-contract';

const RobotProxyConstructor: new () => IRobotAgentProxy = RobotAgentProxy;

export {
    RobotProxyConstructor,
    IRobotAgentProxy,

    // Downstream contract DTOs
    RobotStatusChangedEventArgs,
    ProcessListUpdatedArgs,
    RobotStatus,
    RefreshStatusParameters,

    // Common contract DTOs
    LocalProcessInformation,
    StartJobParameters,
    StopJobParameters
};
