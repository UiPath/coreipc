import { SessionId } from "../../surface/session-id";
import { CancellationToken } from '@uipath/ipc-helpers';

/* @internal */
export interface IChatService {
    startSessionAsync(nickname: string, cancellationToken: CancellationToken): Promise<SessionId>;
    broadcastAsync(sessionId: SessionId, message: string): Promise<number>;
    endSessionAsync(sessionId: SessionId, cancellationToken: CancellationToken): Promise<boolean>;
}

/* @internal */
export class ChatServicePrototype implements IChatService {
    public startSessionAsync(nickname: string, cancellationToken: CancellationToken): Promise<string> { throw null; }
    public broadcastAsync(sessionId: string, message: string): Promise<number> { throw null; }
    public endSessionAsync(sessionId: string, cancellationToken: CancellationToken): Promise<boolean> { throw null; }
}
