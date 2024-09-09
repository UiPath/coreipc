using Microsoft.EntityFrameworkCore;

namespace UiPath.Ipc.TV.DataAccess;

/// <summary>
/// Creates or opens existing SQLite files.
/// </summary>
public class TelemetryContextFactory
{
    public static TelemetryContext Create(string path)
    {
        var options = new DbContextOptionsBuilder<TelemetryContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        var context = new TelemetryContext(options);
        _ = context.Database.EnsureCreated();
        return context;
    }
}