// tslint:disable: no-conditional-assignment no-namespace

import * as fs from 'fs';
import * as path from 'path';
import { spawn, ChildProcessWithoutNullStreams } from 'child_process';
import { Readable, Writable } from 'stream';

import { PromiseCompletionSource, ObjectDisposedError, InvalidOperationError, IDisposable } from '@foundation';

export interface IAsyncDisposable {
    disposeAsync(): Promise<void>;
}

export enum SignalKind {
    Throw = 'Throw',
    PoweringOn = 'PoweringOn',
    ReadyToConnect = 'ReadyToConnect',
}
interface Signal<TDetails = unknown> {
    Kind: SignalKind;
    Details: TDetails;
}
interface SignalExceptionDetails {
    Type: string;
    Message: string;
}

export class DotNetScriptError extends Error {
    public constructor(
        public readonly type: string,
        message: string,
    ) {
        super(message);
        super.name = 'DotNetScriptError';
    }
}

export class DotNetScript implements IAsyncDisposable {
    private _process: ChildProcessWithoutNullStreams | null = null;
    private _processExitError: Error | undefined;
    private _stdin: Writable | null = null;

    private _stderrLog = new Array<string>();
    private _stdoutLog = new Array<string>();

    private _awaiterMap = new Map<SignalKind, PromiseCompletionSource<unknown>>();
    private _disposed = false;
    private _error?: Error;
    private readonly _path: string;
    private static readonly _tempFileName = 'temp.csx';

    constructor(
        private readonly _cwd: string,
        private readonly _code: string,
        private readonly _args: string,
    ) {

        this._path = path.join(_cwd, DotNetScript._tempFileName);

        this.init();
    }

    private init(): void {
        fs.writeFileSync(this._path, this._code);

        const pathDotNet = path.join(process.env['ProgramFiles'] as any, 'dotnet', 'dotnet.exe');
        const dotNetExists = fs.existsSync(pathDotNet);
        if (!fs.existsSync(pathDotNet)) {
            throw new Error(`"dotnet.exe" not found. Probed path is "${pathDotNet}".`);
        }

        if (!fs.existsSync(this._path)) {
            throw new Error(`"${DotNetScript._tempFileName}" not found. Probed path is "${this._path}".`);
        }

        // this._stdoutLog.push(`Starting:\r\n\tcommand: dotnet\r\n\targs: dotnet script temp.csx ${this._args}\r\n\tcwd: ${this._cwd}`);

        this._process = spawn('dotnet', ['script', 'temp.csx', this._args], {
            shell: false,
            cwd: this._cwd,
            stdio: 'pipe',
        });

        this._process.once('close', code => {
            this._processExitError = new InvalidOperationError(
                `Process ${this._process?.pid} exited with code ${code}\r\n\r\n` +
                `$STDERR:\r\n\r\n${this._stderrLog.join('\r\n')}\r\n\r\n` +
                `$STDOUT:\r\n\r\n${this._stdoutLog.join('\r\n')}`);

            for (const awaiter of this._awaiterMap.values()) {
                awaiter.trySetFaulted(this._processExitError);
            }
        });

        this._process.stderr.setEncoding('utf-8').observeLines(this._stderrLog);
        this._process.stdout.setEncoding('utf-8').observeLines(line => {
            this._stdoutLog.push(line);

            const prefix = '###';
            if (line.startsWith(prefix)) {
                const signal = JSON.parse(line.substr(prefix.length)) as Signal;

                if (signal.Kind === SignalKind.Throw) {
                    const details = signal.Details as SignalExceptionDetails;
                    this._error = this._error ?? new DotNetScriptError(details.Type, details.Message);
                    for (const signalKind of this._awaiterMap.keys()) {
                        this._awaiterMap.get(signalKind)?.trySetFaulted(this._error);
                    }
                }

                this.getCompletionSource(signal.Kind).trySetResult(signal.Details);
            }
        });

        this._stdin = this._process.stdin.setDefaultEncoding('utf-8');
    }

    private maybeThrow(): void | never {
        if (this._error) { throw this._error; }
    }

    public async writeLineAsync(line: string): Promise<void> {
        await new Promise<void>((resolve, reject) => {
            this.maybeThrow();

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

    private getCompletionSource<T = unknown>(signalKind: SignalKind): PromiseCompletionSource<T> {
        let result = this._awaiterMap.get(signalKind);
        if (!result) {
            result = new PromiseCompletionSource<T>();
            this._awaiterMap.set(signalKind, result);
        }
        return result as PromiseCompletionSource<T>;
    }

    public async waitForSignal(signalKind: SignalKind): Promise<void> {
        const process = this._process as ChildProcessWithoutNullStreams;

        if (this._disposed || process.killed) {
            throw this._processExitError ?? new ObjectDisposedError('DotNetScript');
        }

        if (this._error) {
            throw this._error;
        }

        return await this.getCompletionSource<void>(signalKind).promise;
    }

    public async disposeAsync(): Promise<void> {
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
