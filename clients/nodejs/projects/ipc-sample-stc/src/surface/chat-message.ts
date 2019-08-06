import { SessionId } from './session-id';

export class ChatMessage {
    public static isSessionCreated(x: ChatMessage): x is ChatSessionCreated { return x instanceof ChatSessionCreated; }
    public static isSessionDestroyed(x: ChatMessage): x is ChatSessionDestroyed { return x instanceof ChatSessionDestroyed; }
    public static isMessageSent(x: ChatMessage): x is ChatMessageSent { return x instanceof ChatMessageSent; }
    
    constructor(public readonly sessionId: SessionId) { }
}

export class ChatSessionCreated extends ChatMessage {
    constructor(sessionId: SessionId, public readonly nickname: string) {
        super(sessionId);
    }
}
export class ChatSessionDestroyed extends ChatMessage {
    constructor(sessionId: SessionId) {
        super(sessionId);
    }
}
export class ChatMessageSent extends ChatMessage {
    constructor(sessionId: SessionId, public readonly message: string) {
        super(sessionId);
    }
}

