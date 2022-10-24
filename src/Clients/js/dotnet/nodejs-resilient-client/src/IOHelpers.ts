import * as fs from 'fs';
import { Readable } from 'stream';

export class IOHelpers {
    public static pipeExists(pipeName: string): boolean {
        const fullPipeName = `\\\\.\\pipe\\${pipeName}`;
        const result = fs.existsSync(fullPipeName);

        return result;
    }
}

export interface IDisposable {
    dispose(): void;
}

declare module 'stream' {
    interface Readable {
        observeLines(destination: string[]): IDisposable;
        observeLines(observer: (line: string) => void): IDisposable;
    }
}

Readable.prototype.observeLines = function (this: Readable, arg0: string[] | ((line: string) => void)): IDisposable {
    if (typeof arg0 === 'function') {
        const observer = arg0;

        let _window = '';

        const handler = (chunk: string) => {
            _window += chunk;
            let index = 0;

            // tslint:disable-next-line: no-conditional-assignment
            while ((index = _window.indexOf('\n')) > 0) {
                const line = _window.substr(0, index).replace(/[\r]+$/, '');
                observer(line);
                _window = _window.substr(index + 1);
            }
        };

        this.on('data', handler);
        return {
            dispose: this.off.bind(this, 'data', handler),
        };
    } else {
        const destination = arg0;

        return this.observeLines(line => destination.push(line));
    }
};
