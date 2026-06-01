//! Phase A — wire codec unit tests.
//!
//! Locks the exact byte layout `[type:u8][length:i32 LE][data]`, the read-fully reassembly
//! semantics, and the message-kind mapping against the .NET/JS implementations.

use bytes::{BufMut, Bytes, BytesMut};
use tokio_util::codec::{Decoder, Encoder};
use uipath_coreipc::wire::{Frame, MessageCodec, MessageKind, WireError, HEADER_LEN};

#[test]
fn message_kind_maps_known_bytes_and_rejects_unknown() {
    assert_eq!(MessageKind::from_u8(0), Some(MessageKind::Request));
    assert_eq!(MessageKind::from_u8(1), Some(MessageKind::Response));
    assert_eq!(MessageKind::from_u8(2), Some(MessageKind::Cancel));
    // 3 = UploadRequest, 4 = DownloadResponse (out of scope), 255 = garbage.
    for unknown in [3u8, 4, 5, 255] {
        assert_eq!(MessageKind::from_u8(unknown), None);
    }
    assert_eq!(MessageKind::Request.as_u8(), 0);
    assert_eq!(MessageKind::Response.as_u8(), 1);
    assert_eq!(MessageKind::Cancel.as_u8(), 2);
}

#[test]
fn encodes_golden_bytes_little_endian() {
    // A Request frame carrying the JSON `{}` (bytes 0x7B 0x7D, length 2).
    // Expected wire bytes: [0x00][0x02 0x00 0x00 0x00][0x7B 0x7D].
    let mut codec = MessageCodec::new();
    let mut dst = BytesMut::new();
    codec
        .encode(
            Frame::new(MessageKind::Request, Bytes::from_static(b"{}")),
            &mut dst,
        )
        .unwrap();
    assert_eq!(&dst[..], &[0x00, 0x02, 0x00, 0x00, 0x00, 0x7B, 0x7D]);

    // Response + Cancel kinds differ only in the first byte.
    let mut dst = BytesMut::new();
    codec
        .encode(
            Frame::new(MessageKind::Response, Bytes::from_static(b"{}")),
            &mut dst,
        )
        .unwrap();
    assert_eq!(dst[0], 0x01);

    let mut dst = BytesMut::new();
    codec
        .encode(
            Frame::new(MessageKind::Cancel, Bytes::from_static(b"{}")),
            &mut dst,
        )
        .unwrap();
    assert_eq!(dst[0], 0x02);
}

#[test]
fn decodes_a_full_frame() {
    let mut codec = MessageCodec::new();
    let mut buf = BytesMut::new();
    buf.put_u8(1);
    buf.put_i32_le(3);
    buf.extend_from_slice(b"abc");

    let frame = codec.decode(&mut buf).unwrap().expect("a full frame");
    assert_eq!(frame.kind, MessageKind::Response);
    assert_eq!(&frame.data[..], b"abc");
    assert!(buf.is_empty(), "decoder must consume the whole frame");
    // No further frame.
    assert!(codec.decode(&mut buf).unwrap().is_none());
}

#[test]
fn reassembles_chunked_and_partial_reads() {
    // The single most important codec property: bytes arriving one-at-a-time, with the
    // header split mid-i32 and the payload split mid-message, must reassemble to one frame.
    let mut codec = MessageCodec::new();
    let payload = b"hello world";
    let mut full = BytesMut::new();
    full.put_u8(0);
    full.put_i32_le(payload.len() as i32);
    full.extend_from_slice(payload);
    let full = full.freeze();

    let mut buf = BytesMut::new();
    // Feed every byte but the last; decoder must keep saying "need more".
    for byte in &full[..full.len() - 1] {
        buf.put_u8(*byte);
        assert!(
            codec.decode(&mut buf).unwrap().is_none(),
            "must not yield a frame before all bytes arrive"
        );
    }
    // Final byte completes the frame.
    buf.put_u8(full[full.len() - 1]);
    let frame = codec
        .decode(&mut buf)
        .unwrap()
        .expect("frame after last byte");
    assert_eq!(frame.kind, MessageKind::Request);
    assert_eq!(&frame.data[..], payload);
}

#[test]
fn decodes_back_to_back_frames_in_one_buffer() {
    let mut codec = MessageCodec::new();
    let mut buf = BytesMut::new();
    for (kind, data) in [(0u8, &b"aa"[..]), (1u8, &b"bbbb"[..])] {
        buf.put_u8(kind);
        buf.put_i32_le(data.len() as i32);
        buf.extend_from_slice(data);
    }

    let f1 = codec.decode(&mut buf).unwrap().expect("first frame");
    assert_eq!(f1.kind, MessageKind::Request);
    assert_eq!(&f1.data[..], b"aa");
    let f2 = codec.decode(&mut buf).unwrap().expect("second frame");
    assert_eq!(f2.kind, MessageKind::Response);
    assert_eq!(&f2.data[..], b"bbbb");
    assert!(buf.is_empty());
    assert!(codec.decode(&mut buf).unwrap().is_none());
}

#[test]
fn handles_zero_length_payload() {
    let mut codec = MessageCodec::new();

    // Encode side: empty payload => 5-byte header, no data.
    let mut dst = BytesMut::new();
    codec
        .encode(Frame::new(MessageKind::Cancel, Bytes::new()), &mut dst)
        .unwrap();
    assert_eq!(&dst[..], &[0x02, 0x00, 0x00, 0x00, 0x00]);
    assert_eq!(dst.len(), HEADER_LEN);

    // Decode side: length 0 yields an empty-data frame, not an error.
    let frame = codec.decode(&mut dst).unwrap().expect("zero-length frame");
    assert_eq!(frame.kind, MessageKind::Cancel);
    assert!(frame.data.is_empty());
}

#[test]
fn rejects_negative_length() {
    let mut codec = MessageCodec::new();
    let mut buf = BytesMut::new();
    buf.put_u8(0);
    buf.put_i32_le(-1);
    buf.extend_from_slice(b"......");
    match codec.decode(&mut buf) {
        Err(WireError::NegativeLength(-1)) => {}
        other => panic!("expected NegativeLength, got {other:?}"),
    }
}

#[test]
fn rejects_oversized_message() {
    let mut codec = MessageCodec::with_max_message_size(4);
    let mut buf = BytesMut::new();
    buf.put_u8(0);
    buf.put_i32_le(100); // claims 100 bytes, exceeds cap of 4
    match codec.decode(&mut buf) {
        Err(WireError::MessageTooLarge { len: 100, max: 4 }) => {}
        other => panic!("expected MessageTooLarge, got {other:?}"),
    }

    // Encode side enforces the same cap.
    let mut dst = BytesMut::new();
    let err = codec
        .encode(
            Frame::new(MessageKind::Request, Bytes::from_static(b"toolong")),
            &mut dst,
        )
        .unwrap_err();
    assert!(matches!(err, WireError::MessageTooLarge { len: 7, max: 4 }));
}

#[test]
fn rejects_unknown_kind_but_stays_byte_aligned() {
    let mut codec = MessageCodec::new();
    let mut buf = BytesMut::new();
    // Unknown kind 9 with a 2-byte payload, followed by a valid frame.
    buf.put_u8(9);
    buf.put_i32_le(2);
    buf.extend_from_slice(b"xy");
    buf.put_u8(1);
    buf.put_i32_le(1);
    buf.extend_from_slice(b"z");

    match codec.decode(&mut buf) {
        Err(WireError::UnknownKind(9)) => {}
        other => panic!("expected UnknownKind, got {other:?}"),
    }
    // The bad frame was consumed; the next valid frame still decodes.
    let frame = codec.decode(&mut buf).unwrap().expect("recovered frame");
    assert_eq!(frame.kind, MessageKind::Response);
    assert_eq!(&frame.data[..], b"z");
}

#[tokio::test]
async fn round_trips_over_an_in_memory_duplex() {
    use futures_util::{SinkExt, StreamExt};
    use tokio::io::duplex;
    use tokio_util::codec::{FramedRead, FramedWrite};

    let (a, b) = duplex(64 * 1024);
    let mut writer = FramedWrite::new(a, MessageCodec::new());
    let mut reader = FramedRead::new(b, MessageCodec::new());

    let frames = vec![
        Frame::new(MessageKind::Request, Bytes::from_static(b"{\"Id\":\"0\"}")),
        Frame::new(MessageKind::Response, Bytes::from_static(b"")),
        Frame::new(
            MessageKind::Cancel,
            Bytes::from_static(b"{\"RequestId\":\"0\"}"),
        ),
    ];

    for f in &frames {
        writer.send(f.clone()).await.unwrap();
    }
    drop(writer);

    let mut received = Vec::new();
    while let Some(item) = reader.next().await {
        received.push(item.unwrap());
    }
    assert_eq!(received, frames);
}
