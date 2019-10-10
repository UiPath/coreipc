import * as net from 'net';
import { LogicalSocket } from './logical-socket';

/* @internal */
export class PhysicalSocket extends LogicalSocket {
    constructor() { super(new net.Socket()); }
}
