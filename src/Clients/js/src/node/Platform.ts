import path from 'path';
import { InvalidOperationError, IPlatform } from '../std';

/* @internal */
export module Platform {
    export const current = create();

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

    function detect(): Id {
        if (process.platform === 'win32') {
            return Id.Windows;
        }

        return Id.Linux;
    }

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

        private static get tempPath(): string {
            const tempEnvVar = 'TMPDIR';
            const defaultTempPath = '/tmp/';
            const tempPath = process.env[tempEnvVar];

            const result = tempPath ?? defaultTempPath;
            return result;
        }
    }
}
