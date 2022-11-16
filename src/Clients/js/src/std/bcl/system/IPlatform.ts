export interface IPlatform<TId> {
    readonly id: TId;

    getFullPipeName(shortName: string): string;
}
