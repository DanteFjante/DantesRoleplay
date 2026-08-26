using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

internal sealed class ObservationTriggerAppendParticipant(
    DantesRoleplayDbContext db,
    ITriggerClock clock) : IObservationAppendTransactionParticipant
{
    public const int MaximumCandidates = 64;

    public async Task StageAsync(StoredObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var candidates = await (from current in db.ObservationTriggerCurrent.AsNoTracking()
            join definition in db.ObservationTriggers.AsNoTracking()
                on new { current.ApplicationId, current.Id, Version = current.CurrentVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where definition.ApplicationId == observation.ApplicationId.Value &&
                definition.Lifecycle == "active" && definition.SourceId == observation.SourceId &&
                definition.SourceVersion == observation.SourceVersion &&
                definition.StructureId == observation.StructureId &&
                definition.StructureVersion == observation.StructureVersion &&
                definition.StructureHash == observation.StructureHash
            orderby definition.Id
            select definition).Take(MaximumCandidates + 1).ToListAsync(cancellationToken);
        if (candidates.Count > MaximumCandidates) throw new TriggerSchedulingContractException(
            "OBSERVATION_TRIGGER_CANDIDATE_LIMIT",
            "At most 64 observation triggers may be staged for one accepted observation.");
        var now = UtcNow();
        foreach (var candidate in candidates)
        {
            var fireId = SqliteObservationTriggerStore.FireId(candidate.ApplicationId, candidate.Id,
                candidate.Version, observation.Id);
            db.ObservationTriggerMatchWork.Add(new ObservationTriggerMatchWorkRecord
            {
                FireId = fireId, ApplicationId = candidate.ApplicationId, TriggerId = candidate.Id,
                TriggerVersion = candidate.Version, ObservationId = observation.Id, State = "ready",
                AttemptCount = 0, Revision = 0, CreatedAtUtc = now.UtcDateTime, UpdatedAtUtc = now.UtcDateTime
            });
        }
        if (candidates.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero) throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC",
            "The trigger clock must use UTC.");
        return now;
    }
}
