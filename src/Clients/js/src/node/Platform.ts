import * as fs from 'fs';
import path from 'path';
import { NamedPipeSocket } from '.';
import {
    CancellationToken,
    InvalidOperationError,
    IPlatform,
    Timeout,
} from '../std';

export module Platform {
    export enum Id {
        Windows,
        Linux,
    }

    class Windows implements IPlatform<Id> {
        get id(): Id {
            return Id.Windows;
        }

        getFullPipeName(shortName: string): string {
            return `\\\\.\\pipe\\${shortName}`;
        }

        async pipeExists(shortName: string): Promise<boolean> {
            return fs.existsSync(this.getFullPipeName(shortName));
        }
    }

    class Linux implements IPlatform<Id> {
        get id(): Id {
            return Id.Linux;
        }

        getFullPipeName(shortName: string): string {
            if (path.isAbsolute(shortName)) {
                // Caller is in full control of file location
                return shortName;
            }
            return `${Linux.tempPath}CoreFxPipe_${shortName}`;
        }

        async pipeExists(shortName: string): Promise<boolean> {
            let socket: NamedPipeSocket | undefined;
            let result: boolean;

            try {
                socket = await NamedPipeSocket.connect(
                    shortName,
                    Timeout.infiniteTimeSpan,
                    CancellationToken.none,
                    undefined,
                );
                result = true;
            } catch (err) {
                result = false;
            }

            if (socket) {
                socket.dispose();
            }
            return result;
        }

        private static get tempPath(): string {
            const tempEnvVar = 'TMPDIR';
            const defaultTempPath = '/tmp/';
            const tempPath = process.env[tempEnvVar];

            const result = tempPath ?? defaultTempPath;
            return result;
        }
    }

    function detect(): Id {
        if (process.platform === 'win32') {
            return Id.Windows;
        }

        return Id.Linux;
    }

    function create(): IPlatform<Id> {
        switch (detect()) {
            case Id.Windows:
                return new Windows();
            case Id.Linux:
                return new Linux();
            default:
                throw new InvalidOperationError();
        }
    }

    export const current = create();
}
