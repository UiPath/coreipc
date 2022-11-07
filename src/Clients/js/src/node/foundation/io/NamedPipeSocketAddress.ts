import { IAddress, CancellationToken, Socket, TimeSpan } from '@foundation';

import { NamedPipeClientSocket, NamedPipeSocketLikeCtor } from '.';

export class NamedPipeSocketAddress implements IAddress {
    constructor(
        public readonly pipeName: string,
        public readonly ctor?: NamedPipeSocketLikeCtor,
    ) {
    }

    public get key(): string { return `pipe/${this.pipeName}`; }

    public async connectAsync(timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        const socket = await NamedPipeClientSocket.connect(this, timeout, ct);
        return socket;
    }
}
