import { EventEmitter } from 'events';
import * as stream from 'stream';

export class LineEmitter extends EventEmitter {
    private _window = '';

    constructor(private readonly _stream: stream.Readable) {
        super();

        _stream.on('data', (chunk: string) => {
            this._window += chunk;
            let index = 0;

            // tslint:disable-next-line: no-conditional-assignment
            while ((index = this._window.indexOf('\n')) > 0) {
                const line = this._window.substr(0, index).replace(/[\r]+$/, '');
                super.emit('line', line);
                this._window = this._window.substr(index + 1);
            }
        });

        _stream.on('end', () => {
            if (this._window) {
                super.emit('line', this._window);
            }
            super.emit('eof');
        });
    }

    public on(event: 'line', listener: (line: string) => void): this;
    public on(event: 'eof', listener: () => void): this;
    public on(event: string, listener: (...args: any[]) => void): this {
        return super.on(event, listener);
    }

}
