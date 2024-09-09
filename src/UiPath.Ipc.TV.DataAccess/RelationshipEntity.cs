using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UiPath.Ipc.TV.DataAccess;

public class RelationshipEntity
{
    public required RecordRelationshipKind Kind { get; init; }

    public string Id1 { get; init; } = null!;
    public string Id2 { get; init; } = null!;

    public RecordEntity Record1 { get; init; } = null!;
    public RecordEntity Record2 { get; init; } = null!;

    public class Config : IEntityTypeConfiguration<RelationshipEntity>
    {
        public void Configure(EntityTypeBuilder<RelationshipEntity> builder)
        {
            builder.HasKey(x => new { x.Kind, x.Id1, x.Id2 });

            builder.HasOne(x => x.Record1).WithMany().HasForeignKey(x => x.Id1);
            builder.HasOne(x => x.Record2).WithMany().HasForeignKey(x => x.Id2);

            builder.HasIndex(x => x.Id1);
            builder.HasIndex(x => x.Id2);

            builder.HasIndex(x => new { x.Id1, x.Kind });
            builder.HasIndex(x => new { x.Id2, x.Kind });
        }
    }
}
