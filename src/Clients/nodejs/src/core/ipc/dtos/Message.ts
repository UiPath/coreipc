// tslint:disable: variable-name

import { TimeSpan } from '@foundation';

export class Message<T = void> {
    constructor(
        public readonly payload?: T,
        requestTimeout?: TimeSpan,
    ) {
        this.requestTimeout = requestTimeout ?? TimeSpan.zero;
    }

    public readonly requestTimeout: TimeSpan;

    public toJSON(): unknown {
        if (this.payload === undefined) {
            return {};
        } else {
            return {
                Payload: this.payload,
            };
        }
    }
}
