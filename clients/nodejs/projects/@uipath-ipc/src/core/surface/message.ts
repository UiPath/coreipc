// tslint:disable: variable-name
export class Message<T> {

    public readonly Payload: T;
    public readonly TimeoutSeconds: number;

    constructor(Payload: T, TimeoutSeconds: number);
    constructor(TimeoutSeconds: number);
    constructor(arg0: T | number, maybeTimeoutSeconds?: number) {
        if (typeof arg0 === 'number' && maybeTimeoutSeconds === undefined){
            this.Payload = undefined as any;
            this.TimeoutSeconds = arg0;
        } else if (typeof maybeTimeoutSeconds === 'number') {
            this.Payload = arg0 as any;
            this.TimeoutSeconds = maybeTimeoutSeconds;
        } else {
            this.Payload = undefined as any;
            this.TimeoutSeconds = undefined as any;
        }
    }

}
