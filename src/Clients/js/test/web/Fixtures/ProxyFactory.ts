import { ipc } from '../../../src/web';
import { serverUrl } from './ServerUrl';

export const proxyFactory = ipc.proxy.withAddress(x => x.isWebSocket(serverUrl));
