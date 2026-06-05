//! [`RpcChannel`]: the bidirectional RPC engine over a single byte stream.
//!
//! ## Concurrency model
//!
//! The `Framed` stream is split into a sink + source. A **single writer task** drains an
//! `mpsc` of outbound frames — this serializes all writes (replacing .NET's `SemaphoreSlim(1)`
//! send-lock) without an `Arc<Mutex<Sink>>`. A **single reader task** demuxes inbound frames:
//!
//! - `Response` → complete the matching pending call (looked up by `RequestId`).
//! - `Request`  → spawn a handler task via the [`Dispatcher`], then write back a `Response`.
//! - `Cancel`   → cancel the matching in-flight handler.
//!
//! The pending/in-flight maps are guarded by `std::sync::Mutex` and only ever locked for the
//! map operation itself — never across an `.await`. On disconnect, all pending calls are
//! failed with [`RpcError::ConnectionClosed`] (by dropping their oneshot senders) and all
//! in-flight handlers are cancelled.

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, AtomicI64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use futures_util::{Sink, SinkExt, Stream, StreamExt};
use tokio::io::{AsyncRead, AsyncWrite};
use tokio::sync::{mpsc, oneshot};
use tokio_util::codec::Framed;
use tokio_util::sync::CancellationToken;

use super::dispatcher::Dispatcher;
use super::error::RpcError;
use super::messages::{WireCancellation, WireRequest, WireResponse};
use crate::wire::{Frame, MessageCodec, MessageKind, WireError};

const WRITE_QUEUE_CAPACITY: usize = 256;

struct ChannelInner {
    tx: mpsc::Sender<Frame>,
    pending: Mutex<HashMap<String, oneshot::Sender<WireResponse>>>,
    inflight: Mutex<HashMap<String, CancellationToken>>,
    next_id: AtomicI64,
    closed: AtomicBool,
    shutdown: CancellationToken,
}

/// A cheaply-cloneable handle to a live RPC channel.
#[derive(Clone)]
pub struct RpcChannel {
    inner: Arc<ChannelInner>,
}

impl RpcChannel {
    /// Start driving the protocol over `stream`, routing incoming requests to `dispatcher`.
    ///
    /// Spawns the reader and writer tasks; returns immediately with a handle.
    pub fn start<S>(stream: S, dispatcher: Arc<dyn Dispatcher>) -> RpcChannel
    where
        S: AsyncRead + AsyncWrite + Send + Unpin + 'static,
    {
        let framed = Framed::new(stream, MessageCodec::new());
        let (sink, source) = framed.split();
        let (tx, rx) = mpsc::channel::<Frame>(WRITE_QUEUE_CAPACITY);

        let inner = Arc::new(ChannelInner {
            tx,
            pending: Mutex::new(HashMap::new()),
            inflight: Mutex::new(HashMap::new()),
            next_id: AtomicI64::new(0),
            closed: AtomicBool::new(false),
            shutdown: CancellationToken::new(),
        });

        tokio::spawn(writer_loop(sink, rx, inner.shutdown.clone()));
        tokio::spawn(reader_loop(source, inner.clone(), dispatcher));

        RpcChannel { inner }
    }

    /// The next per-channel correlation id, as a string (`"0"`, `"1"`, …).
    ///
    /// Matches .NET, whose per-connection counter yields `0` for the first call.
    pub fn next_request_id(&self) -> String {
        self.inner
            .next_id
            .fetch_add(1, Ordering::SeqCst)
            .to_string()
    }

    /// True once the channel has closed (peer disconnect, protocol error, or `shutdown`).
    pub fn is_closed(&self) -> bool {
        self.inner.closed.load(Ordering::SeqCst)
    }

    /// Send a fully-formed request and await its response, honoring `timeout` and `ct`.
    ///
    /// On timeout or cancellation a `Cancel` frame is sent to the peer (best effort) so it
    /// stops working, mirroring .NET's behavior. An error `Response` is mapped to
    /// [`RpcError::Remote`]; a peer disconnect mid-call yields [`RpcError::ConnectionClosed`].
    pub async fn call_raw(
        &self,
        req: WireRequest,
        timeout: Duration,
        ct: CancellationToken,
    ) -> Result<WireResponse, RpcError> {
        self.call_inner(req, Some(timeout), ct).await
    }

    /// Send a request and await its response with NO client-side timeout (cancellable via `ct` only).
    /// Used for `Message.RequestTimeout = InfiniteTimeSpan` calls such as interactive SignIn.
    pub async fn call_raw_infinite(
        &self,
        req: WireRequest,
        ct: CancellationToken,
    ) -> Result<WireResponse, RpcError> {
        self.call_inner(req, None, ct).await
    }

    async fn call_inner(
        &self,
        req: WireRequest,
        timeout: Option<Duration>,
        ct: CancellationToken,
    ) -> Result<WireResponse, RpcError> {
        let inner = &self.inner;
        if inner.closed.load(Ordering::SeqCst) {
            return Err(RpcError::ConnectionClosed);
        }

        let id = req.id.clone();
        let frame = req.to_frame()?;
        let (otx, orx) = oneshot::channel();
        inner.pending.lock().unwrap().insert(id.clone(), otx);

        if inner.tx.send(frame).await.is_err() {
            inner.pending.lock().unwrap().remove(&id);
            return Err(RpcError::ConnectionClosed);
        }

        // `None` => never elapse (infinite wait); `Some(d)` => sleep then time out.
        let timeout_branch = async {
            match timeout {
                Some(d) => tokio::time::sleep(d).await,
                None => std::future::pending::<()>().await,
            }
        };

        let outcome = tokio::select! {
            res = orx => match res {
                Ok(resp) => CallOutcome::Response(resp),
                Err(_) => CallOutcome::Closed, // sender dropped => disconnect
            },
            _ = timeout_branch => CallOutcome::TimedOut,
            _ = ct.cancelled() => CallOutcome::Cancelled,
        };

        match outcome {
            CallOutcome::Response(resp) => match resp.error {
                Some(error) => Err(RpcError::Remote(error.into())),
                None => Ok(resp),
            },
            CallOutcome::Closed => Err(RpcError::ConnectionClosed),
            CallOutcome::TimedOut => {
                inner.pending.lock().unwrap().remove(&id);
                self.send_cancel(&id).await;
                Err(RpcError::Timeout(timeout.unwrap_or_default()))
            }
            CallOutcome::Cancelled => {
                inner.pending.lock().unwrap().remove(&id);
                self.send_cancel(&id).await;
                Err(RpcError::Cancelled)
            }
        }
    }

    /// Send a request frame without registering a pending-response waiter and without awaiting.
    /// The peer's eventual void Response is discarded as orphaned (no entry in `pending`), mirroring
    /// the reference client's fire-and-forget dispatch.
    pub async fn send_oneway(&self, req: WireRequest) -> Result<(), RpcError> {
        let inner = &self.inner;
        if inner.closed.load(Ordering::SeqCst) {
            return Err(RpcError::ConnectionClosed);
        }
        let frame = req.to_frame()?;
        if inner.tx.send(frame).await.is_err() {
            return Err(RpcError::ConnectionClosed);
        }
        Ok(())
    }

    async fn send_cancel(&self, request_id: &str) {
        if let Ok(frame) = (WireCancellation {
            request_id: request_id.to_string(),
        })
        .to_frame()
        {
            let _ = self.inner.tx.send(frame).await;
        }
    }

    /// Gracefully close the channel: stop the reader/writer, fail pending calls, cancel
    /// in-flight handlers, and close the underlying socket.
    pub fn shutdown(&self) {
        close_channel(&self.inner);
    }
}

enum CallOutcome {
    Response(WireResponse),
    Closed,
    TimedOut,
    Cancelled,
}

async fn writer_loop<W>(mut sink: W, mut rx: mpsc::Receiver<Frame>, shutdown: CancellationToken)
where
    W: Sink<Frame, Error = WireError> + Unpin,
{
    loop {
        tokio::select! {
            _ = shutdown.cancelled() => break,
            maybe = rx.recv() => match maybe {
                Some(frame) => {
                    if sink.send(frame).await.is_err() {
                        break;
                    }
                }
                None => break,
            },
        }
    }
    let _ = sink.close().await;
}

async fn reader_loop<R>(mut source: R, inner: Arc<ChannelInner>, dispatcher: Arc<dyn Dispatcher>)
where
    R: Stream<Item = Result<Frame, WireError>> + Unpin,
{
    loop {
        tokio::select! {
            _ = inner.shutdown.cancelled() => break,
            item = source.next() => match item {
                Some(Ok(frame)) => handle_frame(frame, &inner, &dispatcher),
                Some(Err(_)) | None => break,
            },
        }
    }
    close_channel(&inner);
}

fn handle_frame(frame: Frame, inner: &Arc<ChannelInner>, dispatcher: &Arc<dyn Dispatcher>) {
    tracing::trace!(kind = ?frame.kind, len = frame.data.len(), "received frame");
    match frame.kind {
        MessageKind::Response => {
            if let Ok(resp) = serde_json::from_slice::<WireResponse>(&frame.data) {
                if let Some(sender) = inner.pending.lock().unwrap().remove(&resp.request_id) {
                    let _ = sender.send(resp);
                }
            }
        }
        MessageKind::Request => {
            let req: WireRequest = match serde_json::from_slice(&frame.data) {
                Ok(req) => req,
                Err(_) => return,
            };
            let token = CancellationToken::new();
            inner
                .inflight
                .lock()
                .unwrap()
                .insert(req.id.clone(), token.clone());

            let inner = inner.clone();
            let dispatcher = dispatcher.clone();
            tokio::spawn(async move {
                let result = dispatcher
                    .dispatch(&req.endpoint, &req.method_name, req.parameters, token)
                    .await;
                inner.inflight.lock().unwrap().remove(&req.id);

                let resp = match result {
                    Ok(data) => WireResponse {
                        request_id: req.id,
                        data,
                        error: None,
                    },
                    Err(remote) => WireResponse {
                        request_id: req.id,
                        data: None,
                        error: Some(remote.into()),
                    },
                };
                if let Ok(frame) = resp.to_frame() {
                    let _ = inner.tx.send(frame).await;
                }
            });
        }
        MessageKind::Cancel => {
            if let Ok(cancel) = serde_json::from_slice::<WireCancellation>(&frame.data) {
                if let Some(token) = inner.inflight.lock().unwrap().get(&cancel.request_id) {
                    token.cancel();
                }
            }
        }
    }
}

fn close_channel(inner: &Arc<ChannelInner>) {
    inner.closed.store(true, Ordering::SeqCst);
    inner.shutdown.cancel();
    // Dropping the pending oneshot senders makes each awaiting `call_raw` observe a recv
    // error, which it maps to `ConnectionClosed`.
    inner.pending.lock().unwrap().clear();
    let tokens: Vec<CancellationToken> = inner
        .inflight
        .lock()
        .unwrap()
        .drain()
        .map(|(_, token)| token)
        .collect();
    for token in tokens {
        token.cancel();
    }
}
