import { BrowserWebSocketLike } from '.';

/* @internal */
export type BrowserWebSocketLikeCtor = new (
    url: string | URL,
    protocols?: string | string[]
) => BrowserWebSocketLike;
