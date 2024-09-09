using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UiPath.Ipc.TV.DataAccess;

public class TelemetryContext : DbContext, ITelemetryContext
{
    public DbSet<RecordEntity> Records { get; init; } = null!;
    public DbSet<RelationshipEntity> RelationshipEntities { get; init; } = null!;

    public TelemetryContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RecordEntity.Config());
        modelBuilder.ApplyConfiguration(new RelationshipEntity.Config());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }

    public override void Dispose()
    {
        base.Dispose();
        SqliteConnection.ClearAllPools();
    }

    IQueryable<RecordEntity> ITelemetryContext.Records => Records;

    IQueryable<RelationshipEntity> ITelemetryContext.Relationships => RelationshipEntities;
}

public interface ITelemetryContext
{
    IQueryable<RecordEntity> Records { get; }
    IQueryable<RelationshipEntity> Relationships { get; }
}