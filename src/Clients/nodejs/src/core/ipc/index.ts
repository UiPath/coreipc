// tslint:disable: no-namespace no-internal-module

import { IIpc } from './IIpc';
import { Ipc } from './Ipc';

const ipc: IIpc = new Ipc();

export { IIpc, ipc };
export * from './dtos';
export * from './config-store';
export * from './annotations';
export * from './proxy-source';
export * from './contract-store';
export * from './IIpc';
