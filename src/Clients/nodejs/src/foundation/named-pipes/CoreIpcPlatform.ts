// tslint:disable: no-namespace no-internal-module

import * as fs from 'fs';
import { NamedPipeClientSocket } from './NamedPipeClientSocket';
import { TimeSpan, Timeout, CancellationToken } from '../threading';

/* @internal */
export abstract class CoreIpcPlatform {
    public static current: CoreIpcPlatform;

    public getFullName(shortName: string): string { throw void 0; }
    public pipeExists(shortName: string): Promise<boolean> { throw void 0; }
    public getDefaultDotNet(): string { throw void 0; }
    public useShellInUnitTests(): boolean { throw void 0; }
}

/* @internal */
export module CoreIpcPlatform {
    export class Auto extends CoreIpcPlatform {
        constructor() {
            super();
            if (process.platform === 'win32') {
                this._actual = new Windows();
            } else {
                this._actual = new DotNetCoreLinux();
            }
        }

        public getFullName(shortName: string): string { return this._actual.getFullName(shortName); }
        public pipeExists(shortName: string): Promise<boolean> { return this._actual.pipeExists(shortName); }
        public getDefaultDotNet(): string { return this._actual.getDefaultDotNet(); }
        public useShellInUnitTests(): boolean { return this._actual.useShellInUnitTests(); }

        private readonly _actual: CoreIpcPlatform;
    }

    export class Windows extends CoreIpcPlatform {
        public getFullName(shortName: string): string { return `\\\\.\\pipe\\${shortName}`; }
        public async pipeExists(shortName: string): Promise<boolean> {
            return fs.existsSync(this.getFullName(shortName));
        }
        public getDefaultDotNet(): string { return 'dotnet'; }
        public useShellInUnitTests(): boolean { return false; }
    }

    export class DotNetCoreLinux extends CoreIpcPlatform {
        public getFullName(shortName: string): string { return `/tmp/CoreFxPipe_${shortName}`; }
        public async pipeExists(shortName: string): Promise<boolean> {
            let socket: NamedPipeClientSocket | undefined;
            let result: boolean;

            try {
                socket = await NamedPipeClientSocket.connect(
                    shortName,
                    Timeout.infiniteTimeSpan,
                    CancellationToken.none,
                    undefined,
                    this);
                result = true;
            } catch (err) {
                result = false;
            }

            if (socket) { socket.dispose(); }
            return result;
        }
        public getDefaultDotNet(): string { return '/usr/bin/dotnet'; }
        public useShellInUnitTests(): boolean { return true; }
    }
}

CoreIpcPlatform.current = new CoreIpcPlatform.Auto();
