export class AbstractMemberError extends Error {
    public static readonly defaultMessage = 'The member you are invoking is abstract.';

    /* @internal */
    public static computeMessage(message?: string, memberName?: string): string {
        message = message || AbstractMemberError.defaultMessage;
        if (!memberName) {
            return message;
        } else {
            return `${message}\r\nMember name: ${memberName}`;
        }
    }

    constructor(message?: string, memberName?: string) { super(AbstractMemberError.computeMessage(message, memberName)); }
}
