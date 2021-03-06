// tslint:disable: variable-name

import { TimeSpan, Timeout } from '../../../foundation';

export class Message<T = void> {
    constructor(args?: {
        payload?: T,
        requestTimeout?: TimeSpan,
    }) {
        this.Payload = args?.payload;
        this.RequestTimeout = args?.requestTimeout ?? null;
    }

    public readonly Payload?: T;
    public RequestTimeout: TimeSpan | null;
}
