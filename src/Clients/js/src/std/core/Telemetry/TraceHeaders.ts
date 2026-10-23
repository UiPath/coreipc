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

    export function validated(value: TraceContext): TraceContext {
        if (value.tracestate && countMembers(value.tracestate) > maxTraceStateMembers) {
            throw new ArgumentError(`The ${tracestate} must have at most ${maxTraceStateMembers} list members.`, tracestate);
        }
        if (value.baggage) {
            if (countMembers(value.baggage) > maxBaggageMembers) {
                throw new ArgumentError(`The ${baggage} must have at most ${maxBaggageMembers} list members.`, baggage);
            }
            if (new TextEncoder().encode(value.baggage).length > maxBaggageBytes) {
                throw new ArgumentError(`The ${baggage} must be at most ${maxBaggageBytes} bytes (UTF-8).`, baggage);
            }
        }
        return normalize(value.traceparent, value.tracestate, value.baggage);
    }

    export function toHeaders(value: TraceContext): RequestHeaders {
        const headers: RequestHeaders = { [traceparent]: value.traceparent };
        if (value.tracestate) {
            headers[tracestate] = value.tracestate;
        }
        if (value.baggage) {
            headers[baggage] = value.baggage;
        }
        return headers;
    }

    /** Returns undefined when no traceparent arrived. */
    export function fromHeaders(headers: RequestHeaders | undefined): TraceContext | undefined {
        if (headers == null) {
            return undefined;
        }

        const receivedTraceParent = getHeader(headers, traceparent);
        if (receivedTraceParent == null) {
            return undefined;
        }
        return normalize(receivedTraceParent, getHeader(headers, tracestate), getHeader(headers, baggage));
    }

    /** Builds the frozen TraceContext from the values as given, without validating them. */
    export function normalize(traceParentValue: string, traceStateValue?: string | null, baggageValue?: string | null): TraceContext {
        const result: TraceContext = { traceparent: traceParentValue };
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

    export function decodeBaggage(value: string): Array<[string, string]> {
        return value.split(',').map((member): [string, string] => {
            const separator = member.indexOf('=');
            return [unescape(member.substring(0, separator)), unescape(member.substring(separator + 1))];
        });
    }

    function countMembers(list: string): number {
        return list.split(',').length;
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

    const utf8Escape = /%[0-7][0-9a-f]|%[cd][0-9a-f]%[89ab][0-9a-f]|%e[0-9a-f](?:%[89ab][0-9a-f]){2}|%f[0-7](?:%[89ab][0-9a-f]){3}/gi;

    // Like .NET's Uri.UnescapeDataString: an escape that is not valid UTF-8 stays as it is.
    function unescape(value: string): string {
        return value.replace(utf8Escape, sequence => {
            try {
                return decodeURIComponent(sequence);
            } catch {
                return sequence;
            }
        });
    }
}
