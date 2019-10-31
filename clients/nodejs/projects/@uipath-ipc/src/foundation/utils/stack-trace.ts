export class StackTrace {
    private readonly _toString: string;
    public readonly frames: readonly string[];

    constructor() {
        this.frames = (Error().stack || '\r\n')
            .replace('\r\n', '\n')
            .split('\n')
            .filter((line, index) => index > 1);

        this._toString = this.frames.join('\r\n');
    }

    public toString(): string { return this._toString; }
}
