export interface IPlatform<TId> {
    readonly id: TId;

    getFullPipeName(shortName: string): string;
    pipeExists(shortName: string): Promise<boolean>;
}
