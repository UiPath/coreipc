import { CancellationToken } from '@uipath/ipc-helpers';
import { Message } from './message';

/* internal */
export interface IExecutionParams {
    readonly methodName: string;
    readonly jsonArgs: string[];
    readonly maybeCancellationToken: CancellationToken | null;
    readonly maybeMessage: Message<any> | null;
}

/* @internal */
export interface IExecutor {
    executeAsync(executionParams: IExecutionParams): Promise<any>;
}
