using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Data;
using System.Linq.Expressions;
using UiPath.Ipc.TV.DataAccess;

namespace UiPath.Ipc.TV;

public class RecordRepo : IAsyncDisposable
{
    public static async Task<RecordRepo> Create(DirectoryInfo dir, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var errors = new List<(RecordInfo info, string json, Exception error)>();
            var files = dir.EnumerateFiles("*.ndjson", SearchOption.TopDirectoryOnly);

            var dbPath = Path.Combine(dir.FullName, "index.sqlite");
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            var dbContext = TelemetryContextFactory.Create(dbPath);
            dbContext.Database.OpenConnection();

            const int BatchSize = 1;
            const string TableNameRecords = "Records";

            foreach (var file in files)
            {
                var commandPragma = dbContext.Database.GetDbConnection().CreateCommand();
                commandPragma.CommandText = "PRAGMA journal_mode = OFF";
                commandPragma.ExecuteNonQuery();

                using (var transaction = dbContext.Database.BeginTransaction())
                {
                    var commandInsert = dbContext.Database.GetDbConnection().CreateCommand();
                    commandInsert.CommandText = $$"""
                        INSERT INTO {{TableNameRecords}}
                        (Id, CreatedAtUtc, FileName, RecordIndex, RecordKind, RecordJson)
                        VALUES 
                        ($Id, $CreatedAtUtc, $FileName, $RecordIndex, $RecordKind, $RecordJson)
                        """;
                    var paramId = commandInsert.CreateParameter();
                    paramId.ParameterName = "$Id";
                    commandInsert.Parameters.Add(paramId);

                    var paramCreatedAtUtc = commandInsert.CreateParameter();
                    paramCreatedAtUtc.DbType = DbType.DateTime;
                    paramCreatedAtUtc.ParameterName = "$CreatedAtUtc";
                    commandInsert.Parameters.Add(paramCreatedAtUtc);

                    var paramFileName = commandInsert.CreateParameter();
                    paramFileName.ParameterName = "$FileName";
                    commandInsert.Parameters.Add(paramFileName);

                    var paramRecordIndex = commandInsert.CreateParameter();
                    paramRecordIndex.DbType = DbType.Int32;
                    paramRecordIndex.ParameterName = "$RecordIndex";
                    commandInsert.Parameters.Add(paramRecordIndex);

                    var paramVerb = commandInsert.CreateParameter();
                    paramVerb.ParameterName = "$RecordKind";
                    commandInsert.Parameters.Add(paramVerb);

                    var paramRecordJson = commandInsert.CreateParameter();
                    paramRecordJson.ParameterName = "$RecordJson";
                    commandInsert.Parameters.Add(paramRecordJson);

                    foreach (var (json, info, index, verb) in Read(file))
                    {
                        paramId.Value = Guid.Parse(info.Id).ToString();
                        paramCreatedAtUtc.Value = info.CreatedAtUtc;
                        paramFileName.Value = file.Name;
                        paramRecordIndex.Value = index;
                        paramVerb.Value = verb;
                        paramRecordJson.Value = json;

                        try
                        {
                            commandInsert.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            errors.Add((info, json, ex));
                        }
                    }

                    transaction.Commit();
                }

                using (var transaction = dbContext.Database.BeginTransaction())
                {
                    var commandInsert = dbContext.Database.GetDbConnection().CreateCommand();
                    commandInsert.CommandText = $$"""
                        INSERT INTO RelationshipEntities
                        (Kind, Id1, Id2)
                        VALUES 
                        ($Kind, $Id1, $Id2)
                        """;

                    var paramKind = commandInsert.CreateParameter();
                    paramKind.ParameterName = "$Kind";
                    paramKind.DbType = DbType.Int32;
                    commandInsert.Parameters.Add(paramKind);

                    var paramId1 = commandInsert.CreateParameter();
                    paramId1.ParameterName = "$Id1";
                    commandInsert.Parameters.Add(paramId1);

                    var paramId2 = commandInsert.CreateParameter();
                    paramId2.ParameterName = "$Id2";
                    commandInsert.Parameters.Add(paramId2);

                    foreach (var (_, info, index, _) in Read(file))
                    {
                        foreach (var link in info.Links)
                        {
                            if (link.Id is null) { continue; }

                            if (link.Role switch
                            {
                                RecordLinkRole.Parent => RecordRelationshipKind.Parent,
                                RecordLinkRole.StartOfSuccess => RecordRelationshipKind.SucceededStart,
                                RecordLinkRole.StartOfFailure => RecordRelationshipKind.FailedStart,
                                RecordLinkRole.Cause => RecordRelationshipKind.Cause,
                                RecordLinkRole.Modified => RecordRelationshipKind.Modified,
                                _ => null as RecordRelationshipKind?
                            } is not { } linkKind)
                            {
                                continue;
                            }

                            paramKind.Value = (int)linkKind;
                            paramId1.Value = Guid.Parse(info.Id).ToString();
                            paramId2.Value = Guid.Parse(link.Id).ToString();

                            try
                            {
                                commandInsert.ExecuteNonQuery();
                            }
                            catch
                            {
                            }
                        }
                    }


                    transaction.Commit();
                }
            }

            return new RecordRepo(dbContext);
        });

        IEnumerable<(string json, RecordInfo info, int index, string verb)> Read(FileInfo file)
        {
            using var reader = new StreamReader(file.FullName);

            int index = 0;
            while ((reader.ReadLine()) is { } json)
            {
                var record = JsonConvert.DeserializeObject<Telemetry.RecordBase>(json, Telemetry.Jss)!;
                var verb = record.GetType().Name;
                var info = record.GetInfo();
                yield return (json, info, index, verb);
                index++;
            }
        }
    }

    public IQueryable<RecordEntity> Records => Context.Records;
    public IQueryable<RelationshipEntity> Relationships => Context.RelationshipEntities;

    public TelemetryContext Context { get; }

    private RecordRepo(TelemetryContext dbContext)
    {
        Context = dbContext;
    }

    public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}

public readonly struct RecordOrdererPair
{
    public static RecordOrdererPair Create<TKey>(Expression<Func<RecordEntity, TKey>> keySelector)
    => new()
    {
        Ascending = RecordOrderer.Create(keySelector, ascending: true),
        Descending = RecordOrderer.Create(keySelector, ascending: false),
    };

    public required RecordOrderer Ascending { get; init; }
    public required RecordOrderer Descending { get; init; }
}

public abstract class RecordOrderer
{
    public static readonly RecordOrderer Default = Create(r => r.CreatedAtUtc, ascending: true);

    public static RecordOrderer<TKey> Create<TKey>(Expression<Func<RecordEntity, TKey>> keySelector, bool ascending)
    => new RecordOrderer<TKey>
    {
        KeySelector = keySelector,
        Ascending = ascending,
    };

    public required bool Ascending { get; init; } = true;

    public abstract IQueryable<RecordEntity> Apply(IQueryable<RecordEntity> query);
}
public sealed class RecordOrderer<TKey> : RecordOrderer
{
    public required Expression<Func<RecordEntity, TKey>> KeySelector { get; init; }

    public override IQueryable<RecordEntity> Apply(IQueryable<RecordEntity> query)
    => query.OrderBy(KeySelector);
}

public class RecordRepoViewModel
{
    private readonly RecordRepo _repo;

    //private Expression<Func<RecordEntity, bool>> _predicate = _ => true;

    //public Expression<Func<RecordEntity, bool>> Predicate
    //{
    //    get => _predicate;
    //    set
    //    {
    //        if (_predicate == value)
    //        {
    //            return;
    //        }
    //        _predicate = value;
    //        DataChanged?.Invoke(this, EventArgs.Empty);
    //    }
    //}
    //public RecordOrderer Orderer { get; set; } = RecordOrderer.Default;

    public static readonly QueryableFactory DefaultQueryableFactory = context => context.Records.OrderBy(x => x.CreatedAtUtc);

    private QueryableFactory _queryableFactory = DefaultQueryableFactory;

    public QueryableFactory QueryableFactory
    {
        get => _queryableFactory;
        set
        {
            if (_queryableFactory == value)
            {
                return;
            }
            _queryableFactory = value;
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private CachedEntities? _cache;

    public event EventHandler? DataChanged;

    public RecordRepoViewModel(RecordRepo repo)
    {
        _repo = repo;
    }

    public int GetCount() => _queryableFactory(_repo.Context).Count();

    public void Cache(int first, int last)
    {
        //int take = last - first + 1;
        //var records = Orderer.Apply(_repo.Records.Where(Predicate)).Skip(first).Take(take).ToArray();
        //_cache = new(first, records.Length, records);

        int take = last - first + 1;
        var query = _queryableFactory(_repo.Context);

        var records = query.Skip(first).Take(take).ToArray();
        _cache = new(first, records.Length, records);
    }

    internal RecordEntity Get(int index)
    {
        if (_cache is { } notNull && index >= _cache.Value.Skip && index < _cache.Value.Skip + _cache.Value.Take)
        {
            return _cache.Value.Records[index - _cache.Value.Skip];
        }

        return _queryableFactory(_repo.Context).Skip(index).FirstOrDefault();
    }

    private readonly record struct CachedEntities(int Skip, int Take, RecordEntity[] Records);
}