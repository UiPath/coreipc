import { spawn, ChildProcessWithoutNullStreams } from 'child_process';
import { LineEmitter } from './line-emitter';
import { Writable } from 'stream';
import * as fs from 'fs';
import * as path from 'path';

export interface IAsyncDisposable {
    disposeAsync(): Promise<void>;
}

export class DotNetScript implements IAsyncDisposable {
    private _process: ChildProcessWithoutNullStreams | null = null;
    private _stdin: Writable | null = null;
    private readonly _lines = new Array<string | null>();
    private _awaiter: ((line: string | null) => void) | null = null;
    private _disposed = false;
    private readonly _path: string;
    private readonly _initTask: Promise<void>;

    constructor(
        private readonly _cwd: string,
        private readonly _code: string) {

        this._path = path.join(_cwd, 'temp.csx');

        this._initTask = this.initAsync();
        this._initTask.observe().traceError();
    }

    private async initAsync(): Promise<void> {
        await new Promise((resolve, reject) => {
            fs.writeFile(this._path, this._code, err => {
                if (err) {
                    reject(err);
                } else {
                    resolve();
                }
            });
        });

        console.log(`this._path: ${this._path}`);

        const pathDotNet = path.join(process.env['ProgramFiles'] as any, 'dotnet', 'dotnet.exe');
        const dotNetExists = fs.existsSync(pathDotNet);
        console.log(`###### dotNetExists === ${dotNetExists}`);

        const tempcsxExists = fs.existsSync(this._path);
        console.log(`###### temp.csx exists === ${tempcsxExists}`);

        this._process = spawn('dotnet', ['script', 'temp.csx'], {
            shell: false,
            cwd: this._cwd,
            stdio: 'pipe'
        });

        const lineEmitter = new LineEmitter(this._process.stdout.setEncoding('utf-8'));

        lineEmitter
            .on('line', line => {
                console.log(`>>>>>>>>>> LINE: ${line}`);

                const awaiter = this._awaiter;
                this._awaiter = null;

                if (awaiter) {
                    awaiter(line);
                } else {
                    this._lines.push(line);
                }
            })
            .on('eof', () => {
                this._lines.push(null);
                lineEmitter.removeAllListeners();
            });

        this._process.on('error', error => {
            console.log('######## the child process emitted an error', error);
        });
        this._process.on('close', code => {
            if (this._awaiter) {
                // this._awaiter(null);
            }
            if (this._process) {
                // this._process.stdout.destroy();
            }
            // lineEmitter.emit('eof');
            // lineEmitter.emit('line', null);
            console.log('######## the child process ended with code', code);
        });
        this._stdin = this._process.stdin.setDefaultEncoding('utf-8');
    }

    public async writeLineAsync(line: string): Promise<void> {
        await this._initTask;

        await new Promise<void>((resolve, reject) => {
            if (this._disposed) {
                reject(new Error(`Cannot access a disposed object.\r\nObject name: ${DotNetScript.name}.`));
                return;
            }

            (this._stdin as Writable).write(`${line}\r\n`, maybeError => {
                if (maybeError == null) {
                    resolve();
                } else {
                    reject(maybeError);
                }
            });
        });
    }

    public async readLineAsync(): Promise<string | null> {
        console.log(`!!! readLineAsync()`);

        await this._initTask;
        const result = await new Promise<string | null>((resolve, reject) => {
            if (this._disposed) {
                reject(new Error(`Cannot access a disposed object.\r\nObject name: ${DotNetScript.name}.`));
                return;
            }

            if (this._lines.length > 0) {
                resolve(this._lines.splice(0, 1)[0]);
                return;
            }

            this._awaiter = resolve;
        });

        console.log(`!!! readLineAsync() yields ${result}`);

        return result;
    }

    public async waitForLineAsync(line: string): Promise<void> {
        await this._initTask;

        function isTerminal(x: string | null) {
            return x == null || x === line;
        }

        const process = this._process as ChildProcessWithoutNullStreams;

        while (!process.killed && !isTerminal(await this.readLineAsync())) {
        }
    }

    public async disposeAsync(): Promise<void> {
        await this._initTask;
        if (this._process && !this._process.killed) {
            await new Promise<void>(resolve => {
                (this._process as ChildProcessWithoutNullStreams).kill();
                (this._process as ChildProcessWithoutNullStreams).once('exit', resolve);
            });
        }
        await new Promise<void>((resolve, reject) => fs.unlink(this._path, error => {
            if (!error) {
                resolve();
            } else {
                reject(error);
            }
        }));
    }

}
