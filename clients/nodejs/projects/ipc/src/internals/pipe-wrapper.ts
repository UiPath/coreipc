import * as net from 'net';
import { Observable, ReplaySubject } from 'rxjs';
import { IDisposable } from '@uipath/ipc-helpers';

/* @internal */
export interface IPipeWrapper extends IDisposable {
    readonly signals: Observable<PipeSignal>;
    write(data: Buffer, callback: () => void): void;
}

/* @internal */
export class PipeWrapper implements IPipeWrapper {
    private readonly _signals = new ReplaySubject<PipeSignal>();
    private _maybeError: Error | null = null;

    public get signals(): Observable<PipeSignal> { return this._signals; }

    constructor(private readonly _socket: net.Socket) {
        this._socket.addListener('data', data => this._signals.next(new PipeDataSignal(data)));
        this._socket.addListener('error', error => this._maybeError = error);
        this._socket.addListener('close', _ => this._signals.next(new PipeClosedSignal(this._maybeError)));
    }
    public write(data: Buffer, callback: () => void): void { this._socket.write(data, callback); }
    public dispose(): void {
        this._socket.removeAllListeners('data');
        this._socket.destroy();

        this._signals.next(new PipeClosedSignal(null));
        this._signals.complete();
    }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class PipeSignal {
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class PipeDataSignal extends PipeSignal {
    constructor(public readonly data: Buffer) { super(); }
}

/* @internal */
// tslint:disable-next-line: max-classes-per-file
export class PipeClosedSignal extends PipeSignal {
    constructor(public readonly maybeError: Error | null) { super(); }
}
