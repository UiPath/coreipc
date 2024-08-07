
namespace UiPath.Ipc.Benchmarks;

public enum TechnologyId
{
    Old,
    New
}

public static class TechnologyIdExtensions
{
    public static Technology Create(this TechnologyId id)
    => id switch
    {
        TechnologyId.Old => new Technology.Old(),
        TechnologyId.New => new Technology.New(),
        _ => throw new NotSupportedException()
    };
}

