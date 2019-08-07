import { IChatCallback } from './contract';
import { Observer } from 'rxjs';
import { PromiseHelper } from '@uipath/ipc-helpers';
import { ChatMessage, ChatSessionCreated, ChatSessionDestroyed, ChatMessageSent } from '../surface/chat-message';

/* @internal */
export class ChatCallbackImpl implements IChatCallback {
    constructor(private readonly _callbackObserver: Observer<ChatMessage>) { }

    public ProcessSessionCreatedAsync(sessionId: string, nickname: string): Promise<boolean> {
        this._callbackObserver.next(new ChatSessionCreated(sessionId, nickname));
        return PromiseHelper.fromResult(true);
    }
    public ProcessSessionDestroyedAsync(sessionId: string, nickname: string): Promise<boolean> {
        this._callbackObserver.next(new ChatSessionDestroyed(sessionId, nickname));
        return PromiseHelper.fromResult(true);
    }
    public ProcessMessageSentAsync(sessionId: string, nickname: string, message: string): Promise<boolean> {
        this._callbackObserver.next(new ChatMessageSent(sessionId, nickname, message));
        return PromiseHelper.fromResult(true);
    }
}
