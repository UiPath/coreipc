import * as fs from 'fs';
import * as path from 'path';
import { Writable } from 'stream';

import { PromiseCompletionSource, ObjectDisposedError } from '@foundation';

import { spawn, ChildProcessWithoutNullStreams } from 'child_process';
import { LineEmitter } from './LineEmitter';

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
    private _stdin: Writable | null = null;
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

        console.log(`this._path: ${this._path}`);

        const pathDotNet = path.join(process.env['ProgramFiles'] as any, 'dotnet', 'dotnet.exe');
        const dotNetExists = fs.existsSync(pathDotNet);
        if (!fs.existsSync(pathDotNet)) {
            throw new Error(`"dotnet.exe" not found. Probed path is "${pathDotNet}".`);
        }

        if (!fs.existsSync(this._path)) {
            throw new Error(`"${DotNetScript._tempFileName}" not found. Probed path is "${this._path}".`);
        }

        this._process = spawn('dotnet', ['script', 'temp.csx', this._args], {
            shell: false,
            cwd: this._cwd,
            stdio: 'pipe',
        });

        const lineEmitter = new LineEmitter(this._process.stdout.setEncoding('utf-8'));

        lineEmitter
            .on('line', line => {
                console.debug(`.NET> ${line}`);

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
            })
            .on('eof', () => lineEmitter.removeAllListeners());

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
            throw new ObjectDisposedError('DotNetScript');
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
