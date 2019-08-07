import { NamedPipeClientBuilder, INamedPipeClient, Message } from '@uipath/ipc';
import { ChatServicePrototype, IChatService } from '../internals/contract/chat-service';
import { SessionId } from './session-id';
import { ReplaySubject } from 'rxjs';
import { CancellationToken } from '@uipath/ipc-helpers';
import { ChatMessage } from './chat-message';
import { ChatSessionImpl } from '../internals/chat-session-impl';
import { IChatSession } from './chat-session';
import { ChatCallbackImpl } from '../internals/chat-callback-impl';

export class ChatSessionBuilder {

    public static async createAsync(
        pipeName: string,
        nickname: string,
        cancellationToken: CancellationToken = CancellationToken.default
    ): Promise<IChatSession> {

        const subject = new ReplaySubject<ChatMessage>();
        const callback = new ChatCallbackImpl(subject);
        let namedPipeClient: INamedPipeClient<IChatService> | null = null;
        try {
            namedPipeClient = await NamedPipeClientBuilder.createWithCallbacksAsync(pipeName, new ChatServicePrototype(), callback);

            const message = new Message<string>(nickname);
            message.TimeoutInSeconds = 10;

            const sessionId: SessionId = await namedPipeClient.proxy.StartSessionAsync(message, cancellationToken);
            return new ChatSessionImpl(namedPipeClient, subject, nickname, sessionId);
        } catch (error) {
            if (namedPipeClient != null) {
                await namedPipeClient.disposeAsync();
            }
            throw error;
        }

    }

}
