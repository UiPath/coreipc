/* @internal */
export class Base { }
/* @internal */
export abstract class Request extends Base {
    constructor(
        public readonly methodName: string,
        public readonly args: ReadonlyArray<any>
    ) {
        super();
    }
}
/* @internal */
export class InboundRequest extends Request {
    constructor(
        methodName: string,
        args: ReadonlyArray<any>,
        public readonly timeoutSeconds: number
    ) {
        super(methodName, args);
    }
}
/* @internal */
export class OutboundRequest extends Request {
    constructor(
        public readonly endpointName: string,
        methodName: string,
        args: ReadonlyArray<any>
    ) {
        super(methodName, args);
    }
}
/* @internal */
export class Response extends Base {
    constructor(
        public readonly maybeResult: any,
        public readonly maybeError: Error | null | undefined
    ) {
        super();
    }
}
