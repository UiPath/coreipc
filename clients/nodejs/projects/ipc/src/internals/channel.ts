import { IChannelReader } from './channel-reader';
import { IChannelWriter } from './channel-writer';
import { CancellationToken } from '@uipath/ipc-helpers';

export interface IChannel extends IChannelReader, IChannelWriter { }

export class Channel implements IChannel {
    constructor(
        private readonly _reader: IChannelReader,
        private readonly _writer: IChannelWriter
    ) { }

    // tslint:disable-next-line: max-line-length
    public readBufferAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> { return this._reader.readBufferAsync(buffer, cancellationToken); }
    public writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> { return this._writer.writeAsync(buffer, cancellationToken); }

    public dispose(): void { this._reader.dispose(); }
}
