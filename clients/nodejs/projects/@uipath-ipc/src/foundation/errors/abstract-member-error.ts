export class AbstractMemberError extends Error {
    public static readonly defaultMessage = 'The member you are invoking is abstract.';
    constructor(message?: string) { super(message || AbstractMemberError.defaultMessage); }
}
