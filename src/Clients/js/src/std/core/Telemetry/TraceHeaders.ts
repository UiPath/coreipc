import { ArgumentError } from '../../bcl';
import type { TraceContext } from './TraceContext';

/* @internal */
export type RequestHeaders = { [name: string]: string };

/* @internal */
export module TraceHeaders {
    export const traceparent = 'traceparent';
    export const tracestate = 'tracestate';
    export const baggage = 'baggage';

    const maxTraceStateMembers = 32;
    const maxBaggageMembers = 64;
    const maxBaggageBytes = 8192;

    type Header = typeof traceparent | typeof tracestate | typeof baggage;

    // Trace Context §3.2: lowercase hex, version ff is invalid, non-zero ids; a higher version is read by its first
    // 55 characters, which must then end or be followed by '-' (§3.2.4) and visible ASCII, as in any header.
    const traceParentPattern = /^(?!ff)([0-9a-f]{2})-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}(-[\x21-\x7e]*)?$/;

    // Trace Context §3.3.1.3.
    const traceStateMemberPattern =
        /^(?:[a-z][a-z0-9_\-*/]{0,255}|[a-z0-9][a-z0-9_\-*/]{0,240}@[a-z][a-z0-9_\-*/]{0,13})=[\x20-\x2b\x2d-\x3c\x3e-\x7e]{0,255}[\x21-\x2b\x2d-\x3c\x3e-\x7e]$/;

    export function isValidTraceParent(value: string): boolean {
        const match = traceParentPattern.exec(value);
        return match !== null && !(match[1] === '00' && match[2] !== undefined);
    }

    /** @throws ArgumentError when the traceparent breaks W3C Trace Context §3.2, or when there is neither a traceparent nor a baggage. */
    export function validated(value: TraceContext): TraceContext {
        if (value.traceparent && !isValidTraceParent(value.traceparent)) {
            throw new ArgumentError(`The ${traceparent} must be a W3C traceparent.`, traceparent);
        }
        const result = normalize(value.traceparent, value.tracestate, value.baggage);
        if (!result.traceparent && !result.baggage) {
            throw new ArgumentError(`The trace context must have a ${traceparent} or a ${baggage}.`, 'traceContext');
        }
        return result;
    }

    /** Returns undefined when nothing is left to send. */
    export function toHeaders(value: TraceContext): RequestHeaders | undefined {
        const headers: RequestHeaders = {};
        if (value.traceparent && isValidTraceParent(value.traceparent)) {
            headers[traceparent] = value.traceparent;
            const limitedTraceState = value.tracestate && limitTraceState(value.tracestate);
            if (limitedTraceState) {
                headers[tracestate] = limitedTraceState;
            }
        }
        const limitedBaggage = value.baggage && limitBaggage(value.baggage);
        if (limitedBaggage) {
            headers[baggage] = limitedBaggage;
        }
        return Object.keys(headers).length > 0 ? headers : undefined;
    }

    /**
     * A traceparent that breaks W3C Trace Context is ignored, and so is its tracestate (§3.3); tracestate members that
     * break their grammar are dropped, and the tracestate and baggage are cut to the limits a sender keeps to. Returns
     * undefined when neither a traceparent nor a baggage is left.
     */
    export function fromHeaders(headers: RequestHeaders | undefined): TraceContext | undefined {
        if (headers == null) {
            return undefined;
        }

        const received = getHeader(headers, traceparent);
        const traceParentValue = received && isValidTraceParent(received) ? received : undefined;
        const receivedTraceState = traceParentValue && getHeader(headers, tracestate);
        const traceStateValue = receivedTraceState ? limitTraceState(receivedTraceState) : undefined;
        const receivedBaggage = getHeader(headers, baggage);
        const baggageValue = receivedBaggage ? limitBaggage(receivedBaggage) : undefined;
        return traceParentValue || baggageValue ? normalize(traceParentValue, traceStateValue, baggageValue) : undefined;
    }

    /** Builds the frozen TraceContext from the values as given, without validating them. */
    export function normalize(traceParentValue?: string | null, traceStateValue?: string | null, baggageValue?: string | null): TraceContext {
        const result: TraceContext = {};
        if (traceParentValue) {
            result.traceparent = traceParentValue;
        }
        if (traceStateValue) {
            result.tracestate = traceStateValue;
        }
        if (baggageValue) {
            result.baggage = baggageValue;
        }
        return Object.freeze(result);
    }

    /** W3C Baggage: percent-encoded key=value members joined by ','. */
    export function encodeBaggage(entries: Iterable<[string, string]>): string | undefined {
        const members = new Array<string>();
        for (const [key, value] of entries) {
            // A member with no key is dropped, as limitBaggage drops one with an empty key.
            if (key == null) {
                continue;
            }
            try {
                members.push(`${escape(key)}=${escape(value)}`);
            } catch {
                // So is one String() can't write: OpenTelemetry keeps any value plain JS gives it.
                continue;
            }
        }
        return members.length > 0 ? members.join(',') : undefined;
    }

    /** Skips a member with no key, trims the key and value, and ignores the member's properties (after ';'). */
    export function decodeBaggage(value: string): Array<[string, string]> {
        const entries = new Array<[string, string]>();
        for (const member of value.split(',')) {
            const keyValue = splitBaggageMember(member);
            if (keyValue) {
                entries.push([unescape(keyValue[0]), unescape(keyValue[1])]);
            }
        }
        return entries;
    }

    function splitBaggageMember(member: string): [string, string] | undefined {
        const properties = member.indexOf(';');
        const keyValue = properties < 0 ? member : member.substring(0, properties);
        const separator = keyValue.indexOf('=');
        if (separator < 0) {
            return undefined;
        }
        const key = trimOptionalWhitespace(keyValue.substring(0, separator));
        return key ? [key, trimOptionalWhitespace(keyValue.substring(separator + 1))] : undefined;
    }

    /** At most 64 members and 8192 bytes, dropping whole members beyond that, and the ones a receiver couldn't parse. */
    function limitBaggage(value: string): string | undefined {
        const kept = new Array<string>();
        let bytes = 0;
        for (const raw of toWellFormed(value).split(',')) {
            const member = trimOptionalWhitespace(raw);
            if (!splitBaggageMember(member)) {
                continue;
            }
            // The Buffer that encodes every request: Node's, or the buffer package the web build bundles in. Both count UTF-8
            // exactly here, as toWellFormed left no lone surrogate (the buffer package miscounts some).
            const memberBytes = Buffer.byteLength(member, 'utf8') + (kept.length === 0 ? 0 : 1);
            if (bytes + memberBytes > maxBaggageBytes) {
                continue;
            }
            kept.push(member);
            bytes += memberBytes;
            if (kept.length === maxBaggageMembers) {
                break;
            }
        }
        return joinOrUndefined(kept);
    }

    /** Trace Context §3.3.1.1: at most 32 members, so the first 32 that follow the grammar are kept. */
    function limitTraceState(value: string): string | undefined {
        return joinOrUndefined(validTraceStateMembers(value).slice(0, maxTraceStateMembers));
    }

    function validTraceStateMembers(value: string): string[] {
        return value
            .split(',')
            .map(trimOptionalWhitespace)
            .filter(member => traceStateMemberPattern.test(member));
    }

    // A loop rather than /^[ \t]+|[ \t]+$/, which takes quadratic time on a long run of spaces.
    function trimOptionalWhitespace(value: string): string {
        let start = 0;
        let end = value.length;
        while (start < end && isOptionalWhitespace(value.charCodeAt(start))) {
            start++;
        }
        while (end > start && isOptionalWhitespace(value.charCodeAt(end - 1))) {
            end--;
        }
        return start === 0 && end === value.length ? value : value.substring(start, end);
    }

    function isOptionalWhitespace(code: number): boolean {
        return code === 0x20 || code === 0x09;
    }

    function joinOrUndefined(members: string[]): string | undefined {
        return members.length > 0 ? members.join(',') : undefined;
    }

    // A value that isn't a string, from a peer that ignores the wire format, is ignored.
    function getHeader(headers: RequestHeaders, name: Header): string | undefined {
        const key = Object.prototype.hasOwnProperty.call(headers, name)
            ? name
            : Object.keys(headers).find(candidate => candidate.toLowerCase() === name);
        const value: unknown = key === undefined ? undefined : headers[key];
        return typeof value === 'string' ? value : undefined;
    }

    // A surrogate pair, else a lone surrogate.
    const surrogates = /[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDFFF]/g;

    // A lone surrogate becomes U+FFFD, as on .NET and Python; a pair stays.
    function toWellFormed(value: string): string {
        return value.replace(surrogates, match => (match.length === 2 ? match : '\uFFFD'));
    }

    // Like .NET's Uri.EscapeDataString, so both sides produce the same bytes. toWellFormed, because encodeURIComponent throws on
    // a lone surrogate. OpenTelemetry keeps any value plain JS puts in its baggage: null and undefined go empty (k=), as on .NET
    // and Python; anything else goes as String() writes it, which can differ from Python's str() (true and True, 1 and 1.0).
    function escape(value: string): string {
        const text = value == null ? '' : String(value);
        return encodeURIComponent(toWellFormed(text)).replace(/[!'()*]/g, c => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);
    }

    const escapes = /(?:%[0-9a-f]{2})+/gi;
    // Created on first use, so loading the package never needs a TextDecoder. ignoreBOM keeps a U+FEFF, as .NET and Python do.
    let utf8Decoder: TextDecoder | undefined;

    // W3C Baggage: a percent-escape that isn't valid UTF-8 decodes to U+FFFD. This needs a TextDecoder, which Node and
    // browsers have, unlike Jest's jsdom; only handing a received baggage to OpenTelemetry decodes. It must stay a TextDecoder,
    // not the Buffer limitBaggage counts with: the buffer package the web build bundles in decodes broken UTF-8 differently,
    // such as two U+FFFD for E2 82 C3 A9, where TextDecoder, .NET and Python give one.
    function unescape(value: string): string {
        return value.replace(escapes, sequence => {
            const bytes = new Uint8Array(sequence.length / 3);
            for (let index = 0; index < bytes.length; index++) {
                bytes[index] = parseInt(sequence.substring(index * 3 + 1, index * 3 + 3), 16);
            }
            utf8Decoder ??= new TextDecoder('utf-8', { ignoreBOM: true });
            return utf8Decoder.decode(bytes);
        });
    }
}
