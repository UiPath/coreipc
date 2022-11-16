// tslint:disable: no-conditional-assignment no-namespace

import * as fs from 'fs';
import { spawn, ChildProcessWithoutNullStreams } from 'child_process';
import { Readable, Writable } from 'stream';

import {
    PromiseCompletionSource,
    ObjectDisposedError,
    InvalidOperationError,
    IDisposable,
} from '../../../src/std';

import { Signal } from '.';

export interface IAsyncDisposable {
    disposeAsync(): Promise<void>;
}

export class DotNetScriptError extends Error {
    public constructor(public readonly type: string, message: string) {
        super(message);
        super.name = 'DotNetScriptError';
    }
}

export class DotNetProcess implements IAsyncDisposable {
    private _process: ChildProcessWithoutNullStreams | null = null;
    private _processExitCode: number | null = null;
    private _processExitError: Error | undefined;
    private _stdin: Writable | null = null;

    private _stderrLog = new Array<string>();
    private _stdoutLog = new Array<string>();

    private _awaiterMap = new Map<
        Signal.Kind,
        PromiseCompletionSource<unknown>
    >();
    private _disposed = false;
    private _error?: Error;

    private readonly _pcsExit = new PromiseCompletionSource<void>();
    public get signalExit(): Promise<void> {
        return this._pcsExit.promise;
    }

    public get processExitCode(): number | null {
        return this._processExitCode;
    }
    public get processExitError(): Error | undefined {
        return this._processExitError;
    }

    private readonly _args: string[];

    constructor(
        private readonly _cwd: string,
        private readonly _entryPath: string,
        ...args: string[]
    ) {
        this._args = args;
        this.init();
    }

    private init(): void {
        if (!fs.existsSync(this._entryPath)) {
            throw new Error(`Executable file "${this._entryPath}" not found.`);
        }

        const spawnArgs = [
            'dotnet',
            [this._entryPath, ...this._args],
            {
                shell: true,
                cwd: this._cwd,
                stdio: 'pipe',
            },
        ];

        console.log('****** SPAWNING DOTNET with args === ', spawnArgs);

        this._process = spawn('dotnet', [this._entryPath, ...this._args], {
            shell: true,
            cwd: this._cwd,
            stdio: 'pipe',
        });

        this._process.once('close', (code) => {
            this._processExitCode = code;

            this._processExitError = new InvalidOperationError(
                `Process ${this._process?.pid} exited with code ${code}\r\n\r\n` +
                    `$STDERR:\r\n\r\n${this._stderrLog.join('\r\n')}\r\n\r\n` +
                    `$STDOUT:\r\n\r\n${this._stdoutLog.join('\r\n')}`
            );

            console.log(
                '***** SPAWNED dotnet returned code ===',
                code,
                '\r\n\r\nprocessExitError is\r\n\r\n',
                this.processExitError
            );

            if (code) {
                this._pcsExit.trySetFaulted(this._processExitError);
            } else {
                this._pcsExit.trySetResult();
            }

            for (const awaiter of this._awaiterMap.values()) {
                awaiter.trySetFaulted(this._processExitError);
            }
        });

        this._process.stderr.setEncoding('utf-8').observeLines(this._stderrLog);
        this._process.stdout.setEncoding('utf-8').observeLines((line) => {
            this._stdoutLog.push(line);

            const prefix = '###';
            if (line.startsWith(prefix)) {
                const signal = JSON.parse(line.substr(prefix.length)) as Signal;

                if (signal.Kind === Signal.Kind.Throw) {
                    const details = signal.Details as Signal.ExceptionDetails;
                    this._error =
                        this._error ??
                        new DotNetScriptError(details.Type, details.Message);
                    for (const signalKind of this._awaiterMap.keys()) {
                        this._awaiterMap
                            .get(signalKind)
                            ?.trySetFaulted(this._error);
                    }
                }

                this.getCompletionSource(signal.Kind).trySetResult(
                    signal.Details
                );
            }
        });

        this._stdin = this._process.stdin.setDefaultEncoding('utf-8');
    }

    private maybeThrow(): void | never {
        if (this._error) {
            throw this._error;
        }
    }

    public async writeLineAsync(line: string): Promise<void> {
        await new Promise<void>((resolve, reject) => {
            this.maybeThrow();

            if (this._disposed) {
                reject(
                    new Error(
                        `Cannot access a disposed object.\r\nObject name: ${DotNetProcess.name}.`
                    )
                );
                return;
            }

            (this._stdin as Writable).write(`${line}\r\n`, (maybeError) => {
                if (maybeError == null) {
                    resolve();
                } else {
                    reject(maybeError);
                }
            });
        });
    }

    private getCompletionSource<T = unknown>(
        signalKind: Signal.Kind
    ): PromiseCompletionSource<T> {
        let result = this._awaiterMap.get(signalKind);
        if (!result) {
            result = new PromiseCompletionSource<T>();
            this._awaiterMap.set(signalKind, result);
        }
        return result as PromiseCompletionSource<T>;
    }

    public async waitForSignal(signalKind: Signal.Kind): Promise<void> {
        const process = this._process as ChildProcessWithoutNullStreams;

        if (this._disposed || process.killed) {
            throw (
                this._processExitError ??
                new ObjectDisposedError('DotNetScript')
            );
        }

        if (this._error) {
            throw this._error;
        }

        return await this.getCompletionSource<void>(signalKind).promise;
    }

    public async disposeAsync(): Promise<void> {
        if (this._process && !this._process.killed) {
            console.log('**** DISPOSING');
            await new Promise<void>((resolve) => {
                (this._process as ChildProcessWithoutNullStreams).kill();
                (this._process as ChildProcessWithoutNullStreams).once(
                    'exit',
                    () => {
                        console.log('**** RESOLVING DISPOSAL');
                        resolve();
                    }
                );
            });
        }
    }
}

declare module 'stream' {
    interface Readable {
        observeLines(destination: string[]): IDisposable;
        observeLines(observer: (line: string) => void): IDisposable;
    }
}

Readable.prototype.observeLines = function (
    this: Readable,
    arg0: string[] | ((line: string) => void)
): IDisposable {
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

        return this.observeLines((line) => destination.push(line));
    }
};
