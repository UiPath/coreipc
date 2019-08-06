import { INamedPipeClient } from '@uipath/ipc';
import { IChatService } from './contract/chat-service';
import { SessionId } from '../surface/session-id';
import { ReplaySubject, Observable } from 'rxjs';
import { filter } from 'rxjs/operators';
import { CancellationToken } from '@uipath/ipc-helpers';
import { ChatMessage, ChatSessionCreated, ChatSessionDestroyed, ChatMessageSent } from '../surface/chat-message';
import { IChatSession } from '../surface/chat-session';

/* @internal */
export class ChatSessionImpl implements IChatSession {
    public readonly sessionCreations$: Observable<ChatSessionCreated>;
    public readonly sessionDestructions$: Observable<ChatSessionDestroyed>;
    public readonly messages$: Observable<ChatMessageSent>;

    constructor(private readonly _client: INamedPipeClient<IChatService>, private readonly _subject: ReplaySubject<ChatMessage>, public readonly nickname: string, public readonly sessionId: SessionId) {
        this.sessionCreations$ = _subject.pipe(filter(ChatMessage.isSessionCreated));
        this.sessionDestructions$ = _subject.pipe(filter(ChatMessage.isSessionDestroyed));
        this.messages$ = _subject.pipe(filter(ChatMessage.isMessageSent));
    }

    public async broadcastAsync(message: string): Promise<void> {
        await this._client.proxy.broadcastAsync(this.sessionId, message);
    }
    public async disposeAsync(): Promise<void> {
        await this._client.proxy.endSessionAsync(this.sessionId, CancellationToken.default);
        await this._client.disposeAsync();
        this._subject.complete();
    }
}
