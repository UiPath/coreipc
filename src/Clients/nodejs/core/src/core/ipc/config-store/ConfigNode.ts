import { TimeSpan, Timeout, ConnectHelper } from '../../../foundation';

/* @internal */
export interface ConfigNode {
    requestTimeout?: TimeSpan;
    allowImpersonation?: boolean;
    connectHelper?: ConnectHelper;
}

/* @internal */
export const configNodeDefaults: ConfigNode = {
    requestTimeout: Timeout.infiniteTimeSpan,
    allowImpersonation: false,
    connectHelper: undefined,
};
