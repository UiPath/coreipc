import { TimeSpan } from '@foundation/threading/timespan';

// tslint:disable: variable-name
export class Message<T> {
    public Payload: T | undefined;
    public RequestTimeout: TimeSpan | null;

    constructor();
    constructor(payload: T);
    constructor(requestTimeout: TimeSpan);
    constructor(payload: T, requestTimeout: TimeSpan);
    constructor(maybePayloadOrRequestTimeout?: T | TimeSpan, maybeRequestTimeout?: TimeSpan) {
        if (maybePayloadOrRequestTimeout === undefined && maybeRequestTimeout === undefined) {
            this.Payload = undefined;
            this.RequestTimeout = null;
        } else if (maybePayloadOrRequestTimeout instanceof TimeSpan && maybeRequestTimeout === undefined) {
            this.Payload = undefined;
            this.RequestTimeout = maybePayloadOrRequestTimeout;
        } else {
            /* istanbul ignore else */
            if (!(maybePayloadOrRequestTimeout instanceof TimeSpan)) {
                this.Payload = maybePayloadOrRequestTimeout as T;
                this.RequestTimeout = maybeRequestTimeout || null;
            } else {
                this.Payload = undefined;
                this.RequestTimeout = null;
            }
        }
    }
}
