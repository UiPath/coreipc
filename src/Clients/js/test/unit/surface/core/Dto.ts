export class Dto {
    constructor(
    public readonly BoolProperty: boolean,
    public readonly IntProperty: number,
    public readonly StringProperty: string | null,
    ) {}

    public async ReturnDto(myDto: Dto): Promise<Dto> { return myDto; }
}
