// #!if target === 'node'
import 'websocket-polyfill';
// #!endif


/* @internal */
export interface WebSocketLike {
    binaryType: 'blob' | 'arraybuffer';

    send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void;
    close(code?: number, reason?: string): void;

    onopen: ((this: WebSocket, ev: Event) => any) | null;
    onclose: ((this: WebSocket, ev: CloseEvent) => any) | null;
    onerror: ((this: WebSocket, ev: Event) => any) | null;
    onmessage: ((this: WebSocket, ev: MessageEvent) => any) | null;
}


