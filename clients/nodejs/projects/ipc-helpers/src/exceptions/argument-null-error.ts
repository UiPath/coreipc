export class ArgumentNullError extends Error {
    constructor(parameterName: string) {
        // istanbul ignore next
        super(`Value cannot be null.\r\nParameter name: ${parameterName}`);
    }
}
