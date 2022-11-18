import { TimeSpan } from '../../bcl';

export class Message<T = number> {
    constructor(args?: { payload?: T; requestTimeout?: TimeSpan }) {
        this.Payload = args?.payload;
        this.RequestTimeout = args?.requestTimeout ?? null;
    }

    public readonly Payload?: T;
    public RequestTimeout: TimeSpan | null;
}
