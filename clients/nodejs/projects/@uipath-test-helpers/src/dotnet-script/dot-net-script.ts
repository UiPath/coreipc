import * as fs from 'fs';
import * as path from 'path';
import * as child_process from 'child_process';
import * as rxjs from 'rxjs';
import * as rxjsops from 'rxjs/operators';
import { v4 } from 'uuid';
import { IAsyncDisposable } from '../commons';

export class DotNetScript implements IAsyncDisposable {
    public static async startAsync(scriptCsx: string, pathDotNetScript: string, pathWorkDir: string): Promise<DotNetScript> {
        const filenameCsx = `${v4()}.temp.csx`;
        const pathCsx = path.join(pathWorkDir, filenameCsx);

        await new Promise<void>((resolve, reject) => {
            fs.writeFile(pathCsx, scriptCsx, maybeError => {
                if (maybeError) {
                    reject(maybeError);
                } else {
                    resolve(undefined);
                }
            });
        });

        const process = child_process.spawn(
            pathDotNetScript,
            [pathCsx],
            { cwd: pathWorkDir }
        );

        return new DotNetScript(scriptCsx, pathDotNetScript, pathCsx, process);
    }

    private readonly _events = new rxjs.Subject<DotNetScriptEvent>();
    private _stdoutRemainder = '';
    private _stderrRemainder = '';

    public get events(): rxjs.Observable<DotNetScriptEvent> { return this._events; }

    private constructor(
        private readonly _scriptCsx: string,
        private readonly _pathDotNetScript: string,
        private readonly _pathCsx: string,
        private readonly _process: child_process.ChildProcessWithoutNullStreams
    ) {
        _process.stdout.on('data', data => {
            const _string = `${this._stdoutRemainder}${data}`;
            _string
                .replace('\r\n', '\n')
                .split('\n')
                .forEach((line, index, lines) => {
                    if (index === lines.length - 1) {
                        this._stderrRemainder = line;
                    } else {
                        console.log(`.net -> node: \x1b[36m${line}\x1b[0m`);

                        this._events.next({ type: 'line', line });
                    }
                });
        });

        _process.stderr.on('data', data => {
            const _string = `${this._stderrRemainder}${data}`;
            _string
                .replace('\r\n', '\n')
                .split('\n')
                .forEach((line, index, lines) => {
                    if (index === lines.length - 1) {
                        this._stderrRemainder = line;
                    } else {
                        console.log(`.net -> node: \x1b[31m${line}\x1b[0m`);

                        this._events.next({ type: 'error', line });
                    }
                });
        });
        _process.on('close', () => {
            this._events.next({ type: 'close' });
            this._events.complete();
        });
    }

    public waitAsync(predicate: (event: DotNetScriptEvent) => boolean): Promise<DotNetScriptEvent> {
        return new Promise<DotNetScriptEvent>(resolve => {
            this
                .events
                .pipe(rxjsops.filter(predicate))
                .subscribe(resolve);
        });
    }

    public sendLineAsync(line: string): Promise<void> {
        console.log(`node -> .net: \x1b[32m${line}\x1b[0m`);

        return new Promise<void>((resolve, reject) => {
            this._process.stdin.write(`${line}\r\n`, maybeError => {
                if (maybeError) {
                    reject(maybeError);
                } else {
                    resolve(undefined);
                }
            });
        });
    }

    public async disposeAsync(): Promise<void> {
        this._process.kill();
        await new Promise<void>(resolve => fs.unlink(this._pathCsx, () => resolve()));
        try {
            await this._events.toPromise();
        } catch (error) { }
    }
}

export interface LineEvent {
    readonly type: 'line';
    readonly line: string;
}
export interface ErrorEvent {
    readonly type: 'error';
    readonly line: string;
}
export interface CloseEvent {
    readonly type: 'close';
}
export type DotNetScriptEvent = LineEvent | ErrorEvent | CloseEvent;
