import { IpcStandard } from '../std/core';
import { IpcImpl, IIpc } from './core/ipc/IIpc';

export * from './core';
export * from './foundation';

export const ipc: IIpc = new IpcImpl(new IpcStandard());

export function greetWeb() {
    console.log('🌎 Hello World from @uipath/coreipc-web');
};
