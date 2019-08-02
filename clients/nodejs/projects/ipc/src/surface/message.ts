export class Message<T> {
    // tslint:disable-next-line: variable-name
    public TimeoutInSeconds: number = 0;

    // tslint:disable-next-line: variable-name
    constructor(public readonly Payload: T) { }
}
