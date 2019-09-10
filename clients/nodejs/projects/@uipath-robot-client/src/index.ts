import * as Contract from './downstream-contract';
import { RobotAgentProxy } from './robot-agent-proxy';

const robotAgentProxyCtor: new () => Contract.IRobotAgentProxy = RobotAgentProxy;

export {
    Contract,
    robotAgentProxyCtor
};
