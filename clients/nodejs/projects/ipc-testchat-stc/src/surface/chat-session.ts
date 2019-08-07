import { SessionId } from './session-id';
import { Observable } from 'rxjs';
import { IAsyncDisposable } from '@uipath/ipc-helpers';
import { ChatSessionCreated, ChatSessionDestroyed, ChatMessageSent } from './chat-message';

export interface IChatSession extends IAsyncDisposable {
    readonly nickname: string;
    readonly sessionId: SessionId;

    readonly sessionCreations$: Observable<ChatSessionCreated>;
    readonly sessionDestructions$: Observable<ChatSessionDestroyed>;
    readonly messages$: Observable<ChatMessageSent>;

    readonly errors$: Observable<Error>;

    broadcastAsync(message: string): Promise<void>;
}
