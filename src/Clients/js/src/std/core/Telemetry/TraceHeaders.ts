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
    const maxTraceStateMemberLength = 128;
    const maxBaggageMembers = 64;
    const maxBaggageBytes = 8192;

    type Header = typeof traceparent | typeof tracestate | typeof baggage;

    // Trace Context §3.2: lowercase hex, version ff is invalid, non-zero ids; a higher version is read by its first
    // 55 characters, which must then end or be followed by '-' (§3.2.4).
    const traceParentPattern = /^(?!ff)([0-9a-f]{2})-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}(-[\s\S]*)?$/;

    // Trace Context §3.3.1.3.
    const traceStateMemberPattern =
        /^(?:[a-z][a-z0-9_\-*/]{0,255}|[a-z0-9][a-z0-9_\-*/]{0,240}@[a-z][a-z0-9_\-*/]{0,13})=[\x20-\x2b\x2d-\x3c\x3e-\x7e]{0,255}[\x21-\x2b\x2d-\x3c\x3e-\x7e]$/;

    export function isValidTraceParent(value: string): boolean {
        const match = traceParentPattern.exec(value);
        return match !== null && !(match[1] === '00' && match[2] !== undefined);
    }

    /** @throws ArgumentError when the traceparent breaks W3C Trace Context §3.2. */
    export function validated(value: TraceContext): TraceContext {
        if (value.traceparent && !isValidTraceParent(value.traceparent)) {
            throw new ArgumentError(`The ${traceparent} must be a W3C traceparent.`, traceparent);
        }
        return normalize(value.traceparent, value.tracestate, value.baggage);
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
     * break their grammar are dropped. Returns undefined when neither a traceparent nor a baggage is left.
     */
    export function fromHeaders(headers: RequestHeaders | undefined): TraceContext | undefined {
        if (headers == null) {
            return undefined;
        }

        const received = getHeader(headers, traceparent);
        const traceParentValue = received && isValidTraceParent(received) ? received : undefined;
        const receivedTraceState = traceParentValue && getHeader(headers, tracestate);
        const traceStateValue = receivedTraceState ? joinOrUndefined(validTraceStateMembers(receivedTraceState)) : undefined;
        const baggageValue = getHeader(headers, baggage) || undefined;
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
            members.push(`${escape(key)}=${escape(value)}`);
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
        const encoder = new TextEncoder();
        const kept = new Array<string>();
        let bytes = 0;
        for (const raw of value.split(',')) {
            const member = trimOptionalWhitespace(raw);
            if (!splitBaggageMember(member)) {
                continue;
            }
            const memberBytes = encoder.encode(member).length + (kept.length === 0 ? 0 : 1);
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

    /** Trace Context §3.3.1.5: past 32 members, drop the ones over 128 characters first, then from the end. */
    function limitTraceState(value: string): string | undefined {
        const members = validTraceStateMembers(value);
        for (let index = members.length - 1; index >= 0 && members.length > maxTraceStateMembers; index--) {
            if (members[index].length > maxTraceStateMemberLength) {
                members.splice(index, 1);
            }
        }
        members.length = Math.min(members.length, maxTraceStateMembers);
        return joinOrUndefined(members);
    }

    function validTraceStateMembers(value: string): string[] {
        return value
            .split(',')
            .map(trimOptionalWhitespace)
            .filter(member => traceStateMemberPattern.test(member));
    }

    function trimOptionalWhitespace(value: string): string {
        return value.replace(/^[ \t]+|[ \t]+$/g, '');
    }

    function joinOrUndefined(members: string[]): string | undefined {
        return members.length > 0 ? members.join(',') : undefined;
    }

    function getHeader(headers: RequestHeaders, name: Header): string | undefined {
        if (Object.prototype.hasOwnProperty.call(headers, name)) {
            return headers[name];
        }
        const key = Object.keys(headers).find(candidate => candidate.toLowerCase() === name);
        return key === undefined ? undefined : headers[key];
    }

    // Like .NET's Uri.EscapeDataString, so both sides produce the same bytes.
    function escape(value: string): string {
        return encodeURIComponent(value).replace(/[!'()*]/g, c => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);
    }

    const escapes = /(?:%[0-9a-f]{2})+/gi;
    const utf8Decoder = new TextDecoder();

    // W3C Baggage: a percent-escape that isn't valid UTF-8 decodes to U+FFFD.
    function unescape(value: string): string {
        return value.replace(escapes, sequence => {
            const bytes = new Uint8Array(sequence.length / 3);
            for (let index = 0; index < bytes.length; index++) {
                bytes[index] = parseInt(sequence.substring(index * 3 + 1, index * 3 + 3), 16);
            }
            return utf8Decoder.decode(bytes);
        });
    }
}
