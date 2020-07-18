// tslint:disable: no-namespace no-internal-module

import { IIpc } from './IIpc';
import { Ipc } from './Ipc';

export { IIpc, Ipc };
export * from './dtos';
export * from './config-store';
export * from './annotations';
export * from './proxy-source';
export * from './contract-store';

export const ipc: IIpc = new Ipc();
