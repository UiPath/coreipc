import { SessionId } from "../../surface/session-id";

/* @internal */
export interface IChatCallback {
    processSessionCreatedAsync(sessionId: SessionId, nickname: string): Promise<void>;
    processSessionDestroyedAsync(sessionId: SessionId): Promise<void>;
    processMessageSentAsync(sessionId: SessionId, message: string): Promise<void>;
}
