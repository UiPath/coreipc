using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UiPath.Ipc.TV.DataAccess;

public class RecordEntity
{
    public required string Id { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required string FileName { get; init; }

    public required int RecordIndex { get; init; }

    public required Telemetry.RecordKind RecordKind { get; init; }

    public required string RecordJson { get; init; }

    public class Config : IEntityTypeConfiguration<RecordEntity>
    {
        public void Configure(EntityTypeBuilder<RecordEntity> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.CreatedAtUtc).HasColumnType("DATETIME").IsRequired();
            builder.HasIndex(x => x.CreatedAtUtc);

            builder.Property(x => x.FileName).HasColumnType("VARCHAR(256)").HasMaxLength(256).IsRequired();

            builder.Property(x => x.RecordJson).IsRequired();
            builder.HasIndex(x => new { x.FileName, x.RecordIndex }).IsUnique();

            builder.Property(x => x.RecordKind)
                .HasConversion(
                    @enum => @enum.ToString(), 
                    @string => (Telemetry.RecordKind)Enum.Parse(typeof(Telemetry.RecordKind), @string))
                .HasColumnType("VARCHAR(60)")
                .HasMaxLength(60).IsRequired();
        }
    }
}
