using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc.TV;

using static RelationalTelemetryModelBuilder;
using static Telemetry;
internal sealed class RelationalTelemetryModelBuilder
{
    public static Task<RelationalTelemetryModel> BuildAsync(RawTelemetryModel input)
    => Task.Run(() =>
    {
        var instance = new RelationalTelemetryModelBuilder(input);
        instance.Run();
        return instance._output ?? throw new InvalidOperationException();
    });

    private readonly RawTelemetryModel _input;
    private RelationalTelemetryModel? _output;

    private readonly Dictionary<RecordId, RelationalRecordBuilder> _recordBuilders = new();
    private RelationalRecordBuilder[]? _timestampOrder;

    private readonly Dictionary<RelationalRecordBuilder, Dictionary<ForwardLinkType, RecordReferenceBuilder>> _forwardLinks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RelationalRecordBuilder, Dictionary<BackwardLinkType, HashSet<RelationalRecordBuilder>>> _backwardLinks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RecordId, int> _idToIndex = new();
    
    public RelationalTelemetryModelBuilder(RawTelemetryModel input)
    {
        _input = input;
    }

    private void Run()
    {
        CreateMap();
        PrepareLinks();
        BuildForwardLinks();
        BuildBackwardLinks();
        OrderRecordBuilders();
        WrapUp();
    }

    private void CreateMap()
    {
        foreach (var (fileInfo, recordLineList) in _input.FileToRecordList)
        {
            foreach (var recordLine in recordLineList)
            {
                var recordBuilder = new RelationalRecordBuilder
                {
                    ParentBuilder = this,
                    FileInfo = fileInfo,
                    RecordLine = recordLine,
                };
                _recordBuilders[recordLine.Record.Id.Value] = recordBuilder;
            }
        }
    }

    private void PrepareLinks()
    {
        foreach (var start in _recordBuilders.Values)
        {
            var pairs = start.RecordLine.Record.EnumerateForwardLinks(Resolve);

            foreach (var (forwardLinkType, reference) in pairs)
            {
                if (!_forwardLinks.TryGetValue(start, out var typeReferenceMap))
                {
                    _forwardLinks[start] = typeReferenceMap = new();
                }
                typeReferenceMap[forwardLinkType] = reference;

                if (reference.Resolved is { } end)
                {
                    if (!_backwardLinks.TryGetValue(end, out var backwardLinks))
                    {
                        _backwardLinks[end] = backwardLinks = new();
                    }
                    backwardLinks.Add(forwardLinkType.ToBackwardLink(), start);
                }
            }
        }
    }

    private void BuildForwardLinks()
    {
        foreach (var recordBuilder in _recordBuilders.Values)
        {
            recordBuilder.BuildForwardLinks();
        }
    }

    private void BuildBackwardLinks()
    {
        foreach (var recordBuilder in _recordBuilders.Values)
        {
            recordBuilder.BuildBackwardLinks();
        }
    }

    private void OrderRecordBuilders()
    {
        _timestampOrder = _recordBuilders.Values.OrderBy(builder => builder).ToArray();

        int index = 0;
        foreach (var builder in _timestampOrder)
        {
            _idToIndex[builder.RecordLine.Record.Id.Value] = index;
            index++;
        }
    }

    private void WrapUp()
    {
        _output = new()
        {
            Records = _recordBuilders.ToDictionary(pair => pair.Key, pair => pair.Value.EnsureBuilt()),
            TimestampOrder = _timestampOrder!.Select(builder => builder.EnsureBuilt()).ToArray(),
            IdToIndex = _idToIndex,
        };

        foreach (var record in _output.Records.Values)
        {
            record.Model = _output;
        }
    }

    private RecordReferenceBuilder Resolve(RecordId recordId)
    {
        _ = _recordBuilders.TryGetValue(recordId, out var maybeBuilder);
        return new(recordId, maybeBuilder);
    }

    internal sealed class RelationalRecordBuilder : IComparable<RelationalRecordBuilder>
    {
        public required RelationalTelemetryModelBuilder ParentBuilder { get; init; }
        public required FileInfo FileInfo { get; init; }
        public required RecordLine RecordLine { get; init; }

        private readonly Lazy<RelationalRecord> _lazyOutput;

        private IReadOnlyDictionary<ForwardLinkType, RecordReference>? _forwardLinks;
        private IReadOnlyDictionary<BackwardLinkType, IReadOnlySet<RelationalRecord>>? _backwardLinks;

        public RelationalRecordBuilder()
        {
            _lazyOutput = new(Build);
        }

        public RelationalRecord EnsureBuilt() => _lazyOutput.Value;

        public void BuildForwardLinks()
        {
            _forwardLinks = BuildForwardLinksCore();
        }

        public void BuildBackwardLinks()
        {
            _backwardLinks = BuildBackwardLinksCore();
        }

        private RelationalRecord Build()
        => new()
        {
            Model = null!,
            Origin = new(FileInfo, RecordLine.LineNumber),
            Record = RecordLine.Record,
            Links = _forwardLinks!,
            ReversedLinks = _backwardLinks!,
        };

        private IReadOnlyDictionary<ForwardLinkType, RecordReference> BuildForwardLinksCore()
        {
            if (!ParentBuilder._forwardLinks.TryGetValue(this, out var forwardLinks))
            {
                return CachedEmptyForwardLinks;
            }

            return forwardLinks.ToDictionary(x => x.Key, x => Build(x.Value));

            RecordReference Build(RecordReferenceBuilder builder)
            {
                if (builder.Resolved is null)
                {
                    return new(builder.Id, Resolved: null);
                }

                return new(builder.Id, builder.Resolved.EnsureBuilt());
            }
        }

        private IReadOnlyDictionary<BackwardLinkType, IReadOnlySet<RelationalRecord>> BuildBackwardLinksCore()
        {
            if (!ParentBuilder._backwardLinks.TryGetValue(this, out var backwardLinks))
            {
                return CachedEmptyBackwardLinks;
            }

            return backwardLinks.ToDictionary(
                pair => pair.Key, 
                pair => pair.Value.Select(builder => builder.EnsureBuilt()).ToHashSet().AsReadOnly());
        }

        private static readonly Dictionary<ForwardLinkType, RecordReference> CachedEmptyForwardLinks = new();
        private static readonly Dictionary<BackwardLinkType, IReadOnlySet<RelationalRecord>> CachedEmptyBackwardLinks = new();

        int IComparable<RelationalRecordBuilder>.CompareTo(RelationalRecordBuilder? other)
        {
            if (other is null) { return 1; } // nulls first (shouldn't exist)

            var timeStampComparison = RecordLine.Record.CreatedAtUtc.CompareTo(other.RecordLine.Record.CreatedAtUtc);
            if (timeStampComparison is not 0)
            {
                return timeStampComparison;
            }

            return RecordLine.LineNumber.CompareTo(other.RecordLine.LineNumber);
        }
    }
}

internal readonly record struct RecordReferenceBuilder(RecordId Id, RelationalRecordBuilder? Resolved);

internal static class RecordBaseExtensions
{
    public static IEnumerable<KeyValuePair<ForwardLinkType, RecordReferenceBuilder>> EnumerateForwardLinks(this RecordBase record, Func<RecordId, RecordReferenceBuilder> resolve)
    {
        if (record.IsSubOperation(out var parent))
        {
            yield return new(ForwardLinkType.Parent, resolve(parent.Value));
        }

        if (record.IsEffect(out var cause))
        {
            yield return new(ForwardLinkType.Cause, resolve(cause.Value));
        }

        if (record.IsModifier(out var modified))
        {
            yield return new(ForwardLinkType.Modified, resolve(modified.Value));
        }

        if (record.IsSuccess(out var succeeded))
        {
            yield return new(ForwardLinkType.Succeeded, resolve(succeeded.Value));
        }

        if (record.IsFailure(out var failed))
        {
            yield return new(ForwardLinkType.Failed, resolve(failed.Value));
        }
    }

    public static bool IsSubOperation(this RecordBase record, [NotNullWhen(returnValue: true)] out Id? parent)
    {
        if (record is Is<SubOperation> { Of: { } notNull })
        {
            parent = notNull;
            return true;
        }

        parent = null;
        return false;
    }

    public static bool IsEffect(this RecordBase record, [NotNullWhen(returnValue: true)] out Id? cause)
    {
        if (record is Is<Effect> { Of: { } notNull })
        {
            cause = notNull;
            return true;
        }

        cause = null;
        return false;
    }

    public static bool IsModifier(this RecordBase record, [NotNullWhen(returnValue: true)] out Id? modified)
    {
        if (record is Is<Modifier> { Of: { } notNull })
        {
            modified = notNull;
            return true;
        }

        modified = null;
        return false;
    }

    public static bool IsSuccess(this RecordBase record, [NotNullWhen(returnValue: true)] out Id? succeeded)
    {
        if (record is Is<Success> { Of: { } notNull })
        {
            succeeded = notNull;
            return true;
        }

        succeeded = null;
        return false;
    }

    public static bool IsFailure(this RecordBase record, [NotNullWhen(returnValue: true)] out Id? failed)
    {
        if (record is Is<Failure> { Of: { } notNull })
        {
            failed = notNull;
            return true;
        }

        failed = null;
        return false;
    }
}

internal static class CollectionExtensions
{
    public static void Add<K, V>(this IDictionary<K, HashSet<V>> dictionary, K key, V value)
    {
        if (!dictionary.TryGetValue(key, out var set))
        {
            dictionary[key] = set = new();
        }
        set.Add(value);
    }

    public static IReadOnlySet<T> AsReadOnly<T>(this HashSet<T> set) => set;
}