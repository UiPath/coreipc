import * as net from 'net';
import { LogicalSocket } from './logical-socket';

export class PhysicalSocket extends LogicalSocket {
    constructor() { super(new net.Socket()); }
}
