export class UnknownError extends Error {
    public static ensureError(candidate: unknown): Error {
        if (candidate instanceof Error) {
            return candidate;
        }

        return new UnknownError(candidate);
    }

    constructor(public readonly thrown: unknown) {
        super('A non-error was thrown.');
    }
}
