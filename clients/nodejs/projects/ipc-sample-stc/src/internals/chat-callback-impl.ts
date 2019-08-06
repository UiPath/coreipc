import { IChatCallback } from './contract';
import { Observer } from 'rxjs';
import { PromiseHelper } from '@uipath/ipc-helpers';
import { ChatMessage, ChatSessionCreated, ChatSessionDestroyed, ChatMessageSent } from '../surface/chat-message';

/* @internal */
export class ChatCallbackImpl implements IChatCallback {    
    constructor(private readonly _callbackObserver: Observer<ChatMessage>) { }
    
    public processSessionCreatedAsync(sessionId: string, nickname: string): Promise<void> {
        this._callbackObserver.next(new ChatSessionCreated(sessionId, nickname));
        return PromiseHelper.completedPromise;
    }
    public processSessionDestroyedAsync(sessionId: string): Promise<void> {
        this._callbackObserver.next(new ChatSessionDestroyed(sessionId));
        return PromiseHelper.completedPromise;
    }
    public processMessageSentAsync(sessionId: string, message: string): Promise<void> {
        this._callbackObserver.next(new ChatMessageSent(sessionId, message));
        return PromiseHelper.completedPromise;
    }
}
