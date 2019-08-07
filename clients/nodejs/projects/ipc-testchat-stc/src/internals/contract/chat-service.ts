import { SessionId } from '../../surface/session-id';
import { CancellationToken } from '@uipath/ipc-helpers';
import { Message } from '@uipath/ipc';

/* @internal */
export interface IChatService {
    StartSessionAsync(nickname: Message<string>, cancellationToken: CancellationToken): Promise<SessionId>;
    BroadcastAsync(sessionId: SessionId, text: string, cancellationToken: CancellationToken): Promise<number>;
    EndSessionAsync(sessionId: SessionId, cancellationToken: CancellationToken): Promise<boolean>;
}

/* @internal */
export class ChatServicePrototype implements IChatService {
    public StartSessionAsync(nickname: Message<string>, cancellationToken: CancellationToken): Promise<string> { throw null; }
    public BroadcastAsync(sessionId: string, text: string, cancellationToken: CancellationToken): Promise<number> { throw null; }
    public EndSessionAsync(sessionId: string, cancellationToken: CancellationToken): Promise<boolean> { throw null; }
}
