using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using UiPath.Ipc.TV.DataAccess;

namespace UiPath.Ipc.TV;

public static class RecordEntityExtensions
{
    public static Telemetry.RecordBase GetTelemetryRecord(this RecordEntity entity)
    => Annex.Get(entity).Record!;

    private sealed class Annex
    {
        private static readonly ConditionalWeakTable<RecordEntity, Annex> Annexes = new();
        public static Annex Get(RecordEntity entity) => Annexes.GetValue(entity, static entity => new Annex(entity));

        private readonly RecordEntity _entity;
        private readonly Lazy<Telemetry.RecordBase?> _record;

        public Telemetry.RecordBase? Record => _record.Value;

        private Annex(RecordEntity entity)
        {
            _record = new(CreateTelemetryRecord);
            _entity = entity;
        }

        private Telemetry.RecordBase? CreateTelemetryRecord()
        => JsonConvert.DeserializeObject<Telemetry.RecordBase>(_entity.RecordJson, Telemetry.Jss);
    }
}
