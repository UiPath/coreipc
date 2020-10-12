// tslint:disable: no-namespace no-internal-module

/* @internal */
export abstract class PipeNameConvention {
    public static current: PipeNameConvention;
    public getFullName(shortName: string): string { throw void 0; }
}

/* @internal */
export module PipeNameConvention {
    export class Auto extends PipeNameConvention {
        constructor() {
            super();
            if (process.platform === 'win32') {
                this._actual = new Windows();
            } else {
                this._actual = new DotNetCoreLinux();
            }
        }

        public getFullName(shortName: string): string { return this._actual.getFullName(shortName); }

        private readonly _actual: PipeNameConvention;
    }

    export class Windows extends PipeNameConvention {
        public getFullName(shortName: string): string { return `\\\\.\\pipe\\${shortName}`; }
    }

    export class DotNetCoreLinux extends PipeNameConvention {
        public getFullName(shortName: string): string { return `/tmp/CoreFxPipe_${shortName}`; }
    }
}

PipeNameConvention.current = new PipeNameConvention.Auto();
