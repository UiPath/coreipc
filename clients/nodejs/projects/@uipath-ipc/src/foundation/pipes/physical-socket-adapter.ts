import * as net from 'net';
import { SocketAdapter } from './socket-adapter';

/* @internal */
export class PhysicalSocket extends SocketAdapter {
    constructor() { super(new net.Socket()); }
}
