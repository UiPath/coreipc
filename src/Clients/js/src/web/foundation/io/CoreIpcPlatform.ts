// tslint:disable: no-namespace no-internal-module

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
    export class Web extends CoreIpcPlatform {
        public getFullPipeName(shortName: string): string { throw void 0; }
        public pipeExists(shortName: string): Promise<boolean> { throw void 0; }
        public getDefaultDotNet(): string { throw void 0; }
        public useShellInUnitTests(): boolean { throw void 0; }
    }
}

CoreIpcPlatform.current = new CoreIpcPlatform.Web();
