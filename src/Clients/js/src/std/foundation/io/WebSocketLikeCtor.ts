import { WebSocketLike } from "@foundation";

/* @internal */
export type WebSocketLikeCtor = new (url: string | URL, protocols?: string | string[]) => WebSocketLike;
