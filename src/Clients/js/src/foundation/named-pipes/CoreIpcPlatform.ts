// tslint:disable: no-namespace no-internal-module

import * as fs from 'fs';
import { NamedPipeClientSocket } from './NamedPipeClientSocket';
import { Timeout, CancellationToken } from '../threading';
import * as path from 'path';

/* @internal */
export abstract class CoreIpcPlatform {
    public static current: CoreIpcPlatform;

    public getFullPipeName(shortName: string): string { throw void 0; }
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

        public getFullPipeName(shortName: string): string { return this._actual.getFullPipeName(shortName); }
        public pipeExists(shortName: string): Promise<boolean> { return this._actual.pipeExists(shortName); }
        public getDefaultDotNet(): string { return this._actual.getDefaultDotNet(); }
        public useShellInUnitTests(): boolean { return this._actual.useShellInUnitTests(); }

        private readonly _actual: CoreIpcPlatform;
    }

    export class Windows extends CoreIpcPlatform {
        public getFullPipeName(shortName: string): string { return `\\\\.\\pipe\\${shortName}`; }
        public async pipeExists(shortName: string): Promise<boolean> {
            return fs.existsSync(this.getFullPipeName(shortName));
        }
        public getDefaultDotNet(): string { return 'dotnet'; }
        public useShellInUnitTests(): boolean { return false; }
    }

    export class DotNetCoreLinux extends CoreIpcPlatform {
        public getFullPipeName(shortName: string): string {
            if (path.isAbsolute(shortName)) {
                // Caller is in full control of file location
                return shortName;
            }
            return `${this.getTempPath()}CoreFxPipe_${shortName}`;
        }
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
        public getDefaultDotNet(): string { return 'dotnet'; }
        public useShellInUnitTests(): boolean { return true; }

        private getTempPath(): string {
            const tempEnvVar = 'TMPDIR';
            const defaultTempPath = '/tmp/';
            const tempPath = process.env[tempEnvVar];

            const result = tempPath || defaultTempPath;
            return result;
        }
    }
}

CoreIpcPlatform.current = new CoreIpcPlatform.Auto();
