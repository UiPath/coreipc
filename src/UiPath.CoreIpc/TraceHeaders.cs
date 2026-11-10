using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UiPath.Ipc;

/// <summary>The W3C rules for the trace headers, the same on .NET, JS (TraceHeaders.ts) and Python (trace_context.py).</summary>
internal static class TraceHeaders
{
    internal const int MaxTraceStateMembers = 32;
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

    internal static bool IsValidTraceParent(string value) => TryParseTraceParent(value, null, out _);

    /// <summary>Whether <paramref name="value"/> follows the rule above (a version 00 traceparent has exactly 55 characters),
    /// and if so its ids and flags, read at their offsets.</summary>
    internal static bool TryParseTraceParent(string? value, string? traceState, out ActivityContext context)
    {
        if (value is null ||
            TraceParent.Match(value) is not { Success: true } match ||
            (value.StartsWith("00", StringComparison.Ordinal) && match.Groups["rest"].Success))
        {
            context = default;
            return false;
        }

        context = new(
            ActivityTraceId.CreateFromString(value.AsSpan(3, 32)),
            ActivitySpanId.CreateFromString(value.AsSpan(36, 16)),
            (ActivityTraceFlags)byte.Parse(value.Substring(53, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            traceState,
            isRemote: true);
        return true;
    }

    /// <summary>The members that follow the tracestate grammar, or <c>null</c> when none does.</summary>
    internal static string? ValidTraceState(this string traceState)
    => JoinOrNull(ValidTraceStateMembers(traceState));

    /// <summary>Trace Context §3.3.1.1: at most 32 members, so only the first 32 that follow the grammar are kept.</summary>
    internal static string? LimitTraceState(this string traceState)
    {
        var members = ValidTraceStateMembers(traceState);
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

    /// <summary>The W3C baggage of <paramref name="activity"/> and its parents, in <see cref="Activity.Baggage"/> order, or <c>null</c> when there is none.</summary>
    internal static string? ToBaggageHeader(this Activity activity)
    => activity.Baggage.Any()
        ? string.Join(",", activity.Baggage.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? "")}"))
        : null;

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

    /// <summary>W3C Baggage: a percent-escape that isn't valid UTF-8 decodes to U+FFFD. For a broken sequence, .NET Framework's
    /// <see cref="Encoding.UTF8"/> can give fewer U+FFFD than .NET, JS and Python do; W3C asks only for U+FFFD.</summary>
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
            Decode();
            result.Append(value[index]);
        }
        Decode();
        return result.ToString();

        void Decode()
        {
            if (bytes.Count > 0)
            {
                result.Append(Encoding.UTF8.GetString(bytes.ToArray()));
                bytes.Clear();
            }
        }

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
}
