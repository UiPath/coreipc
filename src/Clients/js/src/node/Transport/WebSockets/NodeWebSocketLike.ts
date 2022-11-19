import WebSocket from 'ws';

/* @internal */
export interface NodeWebSocketLike {
    binaryType: 'nodebuffer' | 'arraybuffer' | 'fragments';

    send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void;
    close(code?: number, reason?: string): void;

    onopen: ((event: WebSocket.Event) => void) | null;
    onerror: ((event: WebSocket.ErrorEvent) => void) | null;
    onclose: ((event: WebSocket.CloseEvent) => void) | null;
    onmessage: ((event: WebSocket.MessageEvent) => void) | null;
}
