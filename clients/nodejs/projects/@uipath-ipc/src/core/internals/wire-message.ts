/* istanbul ignore file */

export enum Type {
    Request,
    Response
}

export class Base {
}

export class Request extends Base {
    constructor(
        // tslint:disable-next-line: variable-name
        public readonly TimeoutInSeconds: number,
        // tslint:disable-next-line: variable-name
        public readonly Id: string,
        // tslint:disable-next-line: variable-name
        public readonly MethodName: string,
        // tslint:disable-next-line: variable-name
        public readonly Parameters: string[]
    ) {
        super();
    }
}

export class Response extends Base {
    constructor(
        // tslint:disable-next-line: variable-name
        public readonly RequestId: string,
        // tslint:disable-next-line: variable-name
        public readonly Data: string | null,
        // tslint:disable-next-line: no-shadowed-variable tslint:disable-next-line: variable-name
        public readonly Error: Error | null | undefined
    ) {
        super();
    }
}

export class Error {
    constructor(
        // tslint:disable-next-line: variable-name
        public Message: string,
        // tslint:disable-next-line: variable-name
        public StackTrace: string,
        // tslint:disable-next-line: variable-name tslint:disable-next-line: no-shadowed-variable
        public Type: string,
        // tslint:disable-next-line: no-shadowed-variable tslint:disable-next-line: variable-name
        public InnerError: Error | null | undefined
    ) { }
}
