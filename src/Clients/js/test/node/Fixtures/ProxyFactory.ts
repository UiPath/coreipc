import { ipc } from '../../../src/node';
import { serverUrl } from './ServerUrl';

export const proxyFactory = ipc.proxy.withAddress(x => x.isWebSocket(serverUrl));
