import { SessionId } from '../../surface/session-id';

/* @internal */
export interface IChatCallback {
    ProcessSessionCreatedAsync(sessionId: SessionId, nickname: string): Promise<boolean>;
    ProcessSessionDestroyedAsync(sessionId: SessionId, nickname: string): Promise<boolean>;
    ProcessMessageSentAsync(sessionId: SessionId, nickname: string, message: string): Promise<boolean>;
}
