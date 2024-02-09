export module Network {
    export interface Message {
        readonly type: Message.Type;
        readonly data: Buffer;
    }

    export module Message {
        export enum Type {
            Request = 0,
            Response = 1,
            Cancel = 2,
        }
    }
}
