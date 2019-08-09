# UiPath IPC client for Nodejs - User Guide

## Table of contents

1) [Audience](#audience)
2) [Walthrough](#walkthrough)
3) [Test Chat Application Suite](#test-chat-application-suite)
4) [Getting started](#getting-started)

## Audience

This guide showcases the way you can use the `UiPath IPC client for Nodejs` library in order to write strongly typed clients.
If you're a contributor looking for a guide on how to contribute to the library itself check [this document](./Readme-Contributors.md).

## Walkthrough

For a thorough walkthrough read this [doc](./walkthrough/Walkthrough.md)

## Test Chat Application Suite

An easy way to discover library's API is checking out the `Test Chat` client server sample application suite.
The suite is comprised of 3 applications:

  - a .NET server
  - a .NET client
  - a Nodejs client

The 1st two use `UiPath.Ipc` client-server library while the last one uses the `UiPath IPC client for Nodejs` library.

![Test Chat Client Server](./readme-assets/test-chat-client-server.png)

This app suite showcases a simple single-channel chat experience:

![Test Chat Client Server Usage](./readme-assets/test-chat-client-server.gif)

## Getting started

This walkthrough describes the steps required to produce a simple chat client.
Create a new npm package and add the `@uipath/ipc` npm package as a dependency.

Import the following types from `@uipath/ipc`:

``` typescript
import { NamedPipeClientBuilder, INamedPipeClient, Message } from '@uipath/ipc';
```

Definining the contract is done by writing one interface for the callbacks:

``` typescript
type SessionId = string;

/* @internal */
export interface IChatCallback {
    ProcessSessionCreatedAsync(sessionId: SessionId, nickname: string): Promise<boolean>;
    ProcessSessionDestroyedAsync(sessionId: SessionId, nickname: string): Promise<boolean>;
    ProcessMessageSentAsync(sessionId: SessionId, nickname: string, message: string): Promise<boolean>;
}
```

and one interface + _abstract_ class pair for the service (this class will only be used for reflection purposes):

``` typescript
/* @internal */
export interface IChatService {
    StartSessionAsync(
        nickname: Message<string>, 
        cancellationToken: CancellationToken): Promise<SessionId>;
    BroadcastAsync(
        sessionId: SessionId,
        text: string,
        cancellationToken: CancellationToken): Promise<number>;
    EndSessionAsync(
        sessionId: SessionId,
        cancellationToken: CancellationToken): Promise<boolean>;
}

/* @internal */
export class ChatServicePrototype implements IChatService {
    public StartSessionAsync(
        nickname: Message<string>,
        cancellationToken: CancellationToken): Promise<string> { throw null; }
    public BroadcastAsync(
        sessionId: string,
        text: string,
        cancellationToken: CancellationToken): Promise<number> { throw null; }
    public EndSessionAsync(
        sessionId: string,
        cancellationToken: CancellationToken): Promise<boolean> { throw null; }
}
```

> Note that the class will never be called and its sole purpose is that of being reflected upon at runtime
> in order to weave a dynamic proxy.

Implement the callback interface thus defining the client's behaviour to remote calls:

``` typescript
/* @internal */
export class ChatCallbackImpl implements IChatCallback {
    public async ProcessSessionCreatedAsync(
        sessionId: string,
        nickname: string): Promise<boolean> {
        ...
    }
    public async ProcessSessionDestroyedAsync(
        sessionId: string,
        nickname: string): Promise<boolean> {
        ...
    }
    public async ProcessMessageSentAsync(
        sessionId: string,
        nickname: string,
        message: string): Promise<boolean> {
        ...
    }
}
```

And call the `NamedPipeClientBuilder.createWithCallbacksAsync` static method:

``` typescript
const callback = new ChatCallbackImpl();
const pipeName = 'test-char-server-pipe-name';

const namedPipeClient: INamedPipeClient<IChatService> = await NamedPipeClientBuilder.createWithCallbacksAsync(
    pipeName,
    new ChatServicePrototype(),
    callback);
```

The `createWithCallbackAsync` method returns a `Promise<INamedPipeClient>` which should be awaited.
If the promise resolves, the resulting `INamedPipeClient` will represent a connected client which can be used to call the server via the `proxy` property, for example:

``` typescript

const sessionId = await namedPipeClient.proxy.StartSessionAsync('Jerry', CancellationToken.default);

```

> `NamedPipeClientBuilder.createWithCallbackAsync` is a generic static method with two generic arguments, `TService` and `TCallback` and the type of the `INamedPipeClient<IChatService>.proxy` property is `TService`.

Callbacks will be delivered by calling the methods of the `TCallback` class, on the provided instance.