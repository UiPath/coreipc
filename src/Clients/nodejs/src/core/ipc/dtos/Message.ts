// tslint:disable: variable-name

import { TimeSpan } from '../../../foundation';

export class Message<T = void> {
    constructor(args?: {
        payload?: T,
        requestTimeout?: TimeSpan,
    }) {
        this.payload = args?.payload;
        this.requestTimeout = args?.requestTimeout ?? TimeSpan.zero;
    }

    public readonly payload?: T;
    public requestTimeout: TimeSpan;

    public toJSON(): unknown {
        return this.payload !== undefined
            ? { Payload: this.payload }
            : {};
    }
}
