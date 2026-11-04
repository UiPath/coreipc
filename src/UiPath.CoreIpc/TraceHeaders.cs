using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UiPath.Ipc;

/// <summary>The W3C rules for the trace headers, the same on .NET, JS (TraceHeaders.ts) and Python (trace_context.py).</summary>
internal static class TraceHeaders
{
    internal const int MaxTraceStateMembers = 32;
    internal const int MaxTraceStateMemberLength = 128;
    internal const int MaxBaggageMembers = 64;
    internal const int MaxBaggageBytes = 8192;

    // Trace Context §3.2: lowercase hex, version ff is invalid, non-zero ids; a higher version is read by its first
    // 55 characters, which must then end or be followed by '-' (§3.2.4).
    private static readonly Regex TraceParent = new(
        @"^(?!ff)[0-9a-f]{2}-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}(?<rest>-.*)?\z",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    // Trace Context §3.3.1.3.
    private static readonly Regex TraceStateMember = new(
        @"^(?:[a-z][a-z0-9_\-*/]{0,255}|[a-z0-9][a-z0-9_\-*/]{0,240}@[a-z][a-z0-9_\-*/]{0,13})=[\x20-\x2b\x2d-\x3c\x3e-\x7e]{0,255}[\x21-\x2b\x2d-\x3c\x3e-\x7e]\z",
        RegexOptions.CultureInvariant);

    private static readonly char[] OptionalWhitespace = [' ', '\t'];

    internal static bool IsValidTraceParent(string value)
    => TraceParent.Match(value) is { Success: true } match && !(value.StartsWith("00", StringComparison.Ordinal) && match.Groups["rest"].Success);

    /// <summary>Parses a traceparent that <see cref="IsValidTraceParent"/> accepted.</summary>
    internal static ActivityContext ParseTraceParent(string traceParent, string? traceState)
    => new(
        ActivityTraceId.CreateFromString(traceParent.AsSpan(3, 32)),
        ActivitySpanId.CreateFromString(traceParent.AsSpan(36, 16)),
        (ActivityTraceFlags)byte.Parse(traceParent.Substring(53, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
        traceState,
        isRemote: true);

    /// <summary>The members that follow the tracestate grammar, or <c>null</c> when none does.</summary>
    internal static string? ValidTraceState(this string traceState)
    => JoinOrNull(ValidTraceStateMembers(traceState));

    /// <summary>Trace Context §3.3.1.5: past 32 members, drop the ones over 128 characters first, then from the end.</summary>
    internal static string? LimitTraceState(this string traceState)
    {
        var members = ValidTraceStateMembers(traceState);
        for (var index = members.Count - 1; index >= 0 && members.Count > MaxTraceStateMembers; index--)
        {
            if (members[index].Length > MaxTraceStateMemberLength)
            {
                members.RemoveAt(index);
            }
        }
        if (members.Count > MaxTraceStateMembers)
        {
            members.RemoveRange(MaxTraceStateMembers, members.Count - MaxTraceStateMembers);
        }
        return JoinOrNull(members);
    }

    /// <summary>W3C Baggage lets a receiver drop a member it can't parse, so a member with no key is skipped.
    /// Whitespace around the key and value isn't part of them, and the member's properties (after ';') are ignored.</summary>
    internal static List<KeyValuePair<string, string>> DecodeBaggage(this string baggage)
    {
        var items = new List<KeyValuePair<string, string>>();
        foreach (var member in baggage.Split(','))
        {
            if (SplitBaggageMember(member) is (var key, var value))
            {
                items.Add(new(Unescape(key), Unescape(value)));
            }
        }
        return items;
    }

    /// <summary>W3C Baggage: send at most 64 members and 8192 bytes, dropping whole members beyond that, and the ones
    /// a receiver couldn't parse.</summary>
    internal static string? LimitBaggage(this string baggage)
    {
        var kept = new List<string>();
        var bytes = 0;
        foreach (var raw in baggage.Split(','))
        {
            var member = raw.Trim(OptionalWhitespace);
            if (SplitBaggageMember(member) is null)
            {
                continue;
            }
            var memberBytes = Encoding.UTF8.GetByteCount(member) + (kept.Count is 0 ? 0 : 1);
            if (bytes + memberBytes > MaxBaggageBytes)
            {
                continue;
            }
            kept.Add(member);
            bytes += memberBytes;
            if (kept.Count == MaxBaggageMembers)
            {
                break;
            }
        }
        return JoinOrNull(kept);
    }

    /// <summary>W3C Baggage: a percent-escape that isn't valid UTF-8 decodes to U+FFFD.</summary>
    internal static string Unescape(string value)
    {
        if (value.IndexOf('%') < 0)
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var bytes = new List<byte>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' && index + 2 < value.Length && IsHex(value[index + 1]) && IsHex(value[index + 2]))
            {
                bytes.Add(byte.Parse(value.Substring(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                index += 2;
                continue;
            }
            Utf8.Decode(bytes, result);
            result.Append(value[index]);
        }
        Utf8.Decode(bytes, result);
        return result.ToString();

        static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static List<string> ValidTraceStateMembers(string traceState)
    => traceState.Split(',').Select(member => member.Trim(OptionalWhitespace)).Where(member => TraceStateMember.IsMatch(member)).ToList();

    private static (string Key, string Value)? SplitBaggageMember(string member)
    {
        var properties = member.IndexOf(';');
        var keyValue = properties < 0 ? member : member.Substring(0, properties);
        var separator = keyValue.IndexOf('=');
        if (separator < 0)
        {
            return null;
        }
        var key = keyValue.Substring(0, separator).Trim(OptionalWhitespace);
        return key.Length is 0 ? null : (key, keyValue.Substring(separator + 1).Trim(OptionalWhitespace));
    }

    private static string? JoinOrNull(List<string> members) => members.Count is 0 ? null : string.Join(",", members);

    /// <summary>UTF-8 decoding with one U+FFFD per maximal invalid subpart (Unicode §3.9, WHATWG Encoding), which
    /// .NET Framework's <see cref="Encoding.UTF8"/> doesn't follow, so all platforms decode the same.</summary>
    private static class Utf8
    {
        public static void Decode(List<byte> bytes, StringBuilder result)
        {
            var index = 0;
            while (index < bytes.Count)
            {
                var lead = bytes[index++];
                if (lead < 0x80)
                {
                    result.Append((char)lead);
                    continue;
                }

                var (count, codePoint, lower, upper) = lead switch
                {
                    >= 0xC2 and <= 0xDF => (1, lead & 0x1F, 0x80, 0xBF),
                    0xE0 => (2, lead & 0x0F, 0xA0, 0xBF),
                    0xED => (2, lead & 0x0F, 0x80, 0x9F),
                    >= 0xE1 and <= 0xEF => (2, lead & 0x0F, 0x80, 0xBF),
                    0xF0 => (3, lead & 0x07, 0x90, 0xBF),
                    0xF4 => (3, lead & 0x07, 0x80, 0x8F),
                    >= 0xF1 and <= 0xF3 => (3, lead & 0x07, 0x80, 0xBF),
                    _ => (0, 0, 0, 0),
                };

                var valid = count > 0;
                for (var seen = 0; valid && seen < count; seen++)
                {
                    if (index >= bytes.Count || bytes[index] < lower || bytes[index] > upper)
                    {
                        valid = false;
                        break;
                    }
                    codePoint = (codePoint << 6) | (bytes[index++] & 0x3F);
                    (lower, upper) = (0x80, 0xBF);
                }

                if (!valid)
                {
                    result.Append('�');
                }
                else if (codePoint < 0x10000)
                {
                    result.Append((char)codePoint);
                }
                else
                {
                    result.Append(char.ConvertFromUtf32(codePoint));
                }
            }
            bytes.Clear();
        }
    }
}
