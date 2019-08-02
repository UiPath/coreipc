export class InvalidOperationError extends Error {
    constructor() {
        // istanbul ignore next
        super('The operation is invalid given the object\'s current state.');
    }
}
