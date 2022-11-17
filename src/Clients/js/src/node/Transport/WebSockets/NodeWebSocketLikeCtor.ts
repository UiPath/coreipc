import { NodeWebSocketLike } from '.';

/* @internal */
export type NodeWebSocketLikeCtor = new (
    url: string | URL,
    protocols?: string | string[]
) => NodeWebSocketLike;
