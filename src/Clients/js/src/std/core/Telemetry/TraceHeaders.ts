import type { TraceContext } from './TraceContext';

/* @internal */
export type RequestHeaders = { [name: string]: string };

/**
 * The three headers as DiagnosticSource 10's W3C propagator (W3CPropagator.cs) writes and reads them, so .NET, JS and Python
 * send the same bytes. Where a map can't follow it, each baggage key goes once, in map order; broken UTF-8 in a received
 * baggage decodes as TextDecoder does.
 */
/* @internal */
export module TraceHeaders {
    export const traceparent = 'traceparent';
    export const tracestate = 'tracestate';
    export const baggage = 'baggage';

    const maxTraceStateLength = 256;
    const maxBaggageMembers = 64;
    const maxBaggageLength = 8192;

    type Header = typeof traceparent | typeof tracestate | typeof baggage;

    // Lowercase hex, version ff is invalid, non-zero ids. Version 00 is exactly 55 characters; a higher one may go on after a '-'.
    const traceParentPattern = /^(?!ff)([0-9a-f]{2})-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}(-[\s\S]*)?$/;

    // A tracestate member, on its raw text: the key trimmed of SP and HTAB, the value of SP only, and no space inside the value.
    const traceStateMemberPattern = /^[ \t]*[a-z0-9][a-z0-9_\-*/@]*[ \t]*=[ ]*[\x21-\x2b\x2d-\x3c\x3e-\x7e]+[ ]*$/;

    // An RFC 7230 token.
    const baggageKeyPattern = /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/;

    // A received baggage value is skipped when it has a raw character above 0x7F or a '%' without two hex digits after it.
    const invalidBaggageValuePattern = /[^\x00-\x7f]|%(?![0-9a-fA-F]{2})/;

    export function isValidTraceParent(value: string): boolean {
        const match = traceParentPattern.exec(value);
        return match !== null && !(match[1] === '00' && match[2] !== undefined);
    }

    /**
     * The scope's trace context, with its baggage encoded once, here, as .NET writes it. As for a received header, a value that
     * isn't text is left out, and so is a traceparent that breaks W3C Trace Context §3.2, with its tracestate. Undefined when
     * neither a traceparent nor a baggage is left.
     */
    export function validated(value: TraceContext): TraceContext | undefined {
        const text = (name: Header) => (typeof value[name] === 'string' ? (value[name] as string) : undefined);
        const traceParentValue = text(traceparent);
        const validTraceParent = traceParentValue && isValidTraceParent(traceParentValue) ? traceParentValue : undefined;
        const baggageValue = text(baggage);
        const encodedBaggage = baggageValue ? encodeBaggage(decodeBaggage(baggageValue)) : undefined;
        return validTraceParent || encodedBaggage
            ? normalize(validTraceParent, validTraceParent && text(tracestate), encodedBaggage)
            : undefined;
    }

    /**
     * A higher-version traceparent goes as version 00. The baggage goes as it is: `validated` and the bridge have encoded it.
     * Undefined when nothing is left.
     */
    export function toHeaders(value: TraceContext): RequestHeaders | undefined {
        const headers: RequestHeaders = {};
        if (value.traceparent && isValidTraceParent(value.traceparent)) {
            headers[traceparent] = `00${value.traceparent.substring(2, 55)}`;
            const validTraceState = value.tracestate && validateTraceState(value.tracestate);
            if (validTraceState) {
                headers[tracestate] = validTraceState;
            }
        }
        if (value.baggage) {
            headers[baggage] = value.baggage;
        }
        return Object.keys(headers).length > 0 ? headers : undefined;
    }

    /**
     * Reads the exact lowercase names. A traceparent that breaks W3C Trace Context is ignored, and so is its tracestate; the
     * tracestate is cut as on send, and the baggage is kept as received. Undefined when neither a traceparent nor a baggage arrived.
     */
    export function fromHeaders(headers: RequestHeaders | undefined): TraceContext | undefined {
        if (headers == null) {
            return undefined;
        }

        const received = getHeader(headers, traceparent);
        const traceParentValue = received && isValidTraceParent(received) ? received : undefined;
        const receivedTraceState = traceParentValue && getHeader(headers, tracestate);
        const traceStateValue = receivedTraceState ? validateTraceState(receivedTraceState) : undefined;
        const baggageValue = getHeader(headers, baggage);
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

    /**
     * The raw text up to the end of the last valid member, at most 256 characters with the commas; it stops at the first
     * invalid member. Undefined when the first member is invalid.
     */
    export function validateTraceState(value: string): string | undefined {
        let processed = 0;
        while (processed < value.length) {
            const comma = value.indexOf(',', processed);
            const end = comma < 0 ? value.length : comma;
            const next = comma < 0 ? end : end + 1;
            // The length first, so the pattern never sees more than 256 characters.
            if (next > maxTraceStateLength || !traceStateMemberPattern.test(value.substring(processed, end))) {
                break;
            }
            processed = next;
        }
        if (processed > 0 && value[processed - 1] === ',') {
            processed--;
        }
        return processed > 0 ? value.substring(0, processed) : undefined;
    }

    /**
     * Members as `key = value`, joined by ', '. A member whose key isn't a token is skipped; at most 64 are sent, and sending
     * stops at the first member that takes the header, with its ', ', to 8192 characters.
     */
    export function encodeBaggage(entries: Iterable<[string, string]>): string | undefined {
        let encoded = '';
        let count = 0;
        let kept = 0;
        for (const [key, value] of entries) {
            const member = encodeBaggageMember(key, value);
            if (member === undefined) {
                continue;
            }
            encoded += `${member}, `;
            if (encoded.length >= maxBaggageLength) {
                break;
            }
            kept = encoded.length;
            if (++count === maxBaggageMembers) {
                break;
            }
        }
        return kept > 0 ? encoded.substring(0, kept - 2) : undefined;
    }

    function encodeBaggageMember(key: string, value: string): string | undefined {
        const trimmedKey = typeof key === 'string' ? trimOptionalWhitespace(key) : '';
        if (!baggageKeyPattern.test(trimmedKey)) {
            return undefined;
        }
        try {
            return `${trimmedKey} = ${escape(value)}`;
        } catch {
            // A value String() can't write is skipped: OpenTelemetry keeps any value plain JS gives it.
            return undefined;
        }
    }

    /**
     * Stops at a member whose raw text has no '=', or has it first or last. Skips a member whose key isn't a token or whose
     * value can't be decoded. Keys aren't decoded; values are trimmed and decoded, ';' properties included. The first of a
     * repeated key wins.
     */
    export function decodeBaggage(value: string): Array<[string, string]> {
        const entries = new Map<string, string>();
        for (const member of value.split(',')) {
            const separator = member.indexOf('=');
            if (separator <= 0 || separator >= member.length - 1) {
                break;
            }
            const key = trimOptionalWhitespace(member.substring(0, separator));
            const escaped = trimOptionalWhitespace(member.substring(separator + 1));
            if (baggageKeyPattern.test(key) && !invalidBaggageValuePattern.test(escaped) && !entries.has(key)) {
                entries.set(key, unescape(escaped));
            }
        }
        return [...entries];
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

    // An exact name; a value that isn't a string, from a peer that ignores the wire format, is ignored.
    function getHeader(headers: RequestHeaders, name: Header): string | undefined {
        const value: unknown = headers[name];
        return typeof value === 'string' ? value : undefined;
    }

    // A surrogate pair, else a lone surrogate.
    const surrogates = /[\uD800-\uDBFF][\uDC00-\uDFFF]|[\uD800-\uDFFF]/g;

    // A lone surrogate becomes U+FFFD, as on .NET and Python; a pair stays.
    function toWellFormed(value: string): string {
        return value.replace(surrogates, match => (match.length === 2 ? match : '\uFFFD'));
    }

    // The baggage-octets other than '%' stay; every other UTF-8 byte goes as uppercase %XX. encodeURIComponent leaves
    // A-Z a-z 0-9 - _ . ! ~ * ' ( ) as they are, so the replace puts back the rest of the octets it escaped (';' stays escaped).
    // OpenTelemetry keeps any value plain JS puts in its baggage: null and undefined go empty, as on .NET and Python; anything
    // else goes as String() writes it, which can differ from Python's str() (true and True, 1 and 1.0).
    function escape(value: string): string {
        const text = value == null ? '' : trimOptionalWhitespace(String(value));
        return encodeURIComponent(toWellFormed(text)).replace(/%(2[346BF]|3[AC-F]|40|5[BDE]|60|7[BCD])/g, (_, hex: string) =>
            String.fromCharCode(parseInt(hex, 16)),
        );
    }

    const escapes = /(?:%[0-9a-fA-F]{2})+/g;
    // Created on first use, so loading the package never needs a TextDecoder. ignoreBOM keeps a U+FEFF, as .NET and Python do.
    let utf8Decoder: TextDecoder | undefined;

    // A percent-escape that isn't valid UTF-8 decodes to U+FFFD. Only that needs a TextDecoder, which Node and browsers have,
    // unlike Jest's jsdom.
    function unescape(value: string): string {
        return value.replace(escapes, sequence => {
            try {
                return decodeURIComponent(sequence);
            } catch {
                // Not valid UTF-8.
            }
            const bytes = new Uint8Array(sequence.length / 3);
            for (let index = 0; index < bytes.length; index++) {
                bytes[index] = parseInt(sequence.substring(index * 3 + 1, index * 3 + 3), 16);
            }
            utf8Decoder ??= new TextDecoder('utf-8', { ignoreBOM: true });
            return utf8Decoder.decode(bytes);
        });
    }
}
