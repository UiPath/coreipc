export type Result<T> = Succeeded<T> | Faulted | Canceled;

export class Succeeded<T> {
    public readonly isSucceeded = true;
    constructor(public readonly result: T) { }
}
export class Faulted {
    public readonly isSucceeded = false;
    constructor(public readonly error: Error) { }
}
export class Canceled {
    public static readonly instance: Canceled = new Canceled();
    public readonly isSucceeded = false;
    private constructor() { /* */ }
}
