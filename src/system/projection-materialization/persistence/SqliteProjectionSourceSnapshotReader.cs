using System.Data;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections;

/// <summary>Reads state-space authority and all prepared component sources in one SQLite snapshot.</summary>
public sealed class SqliteProjectionSourceSnapshotReader(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces,
    IEntityComponentStore components) : IProjectionSourceSnapshotReader
{
    public async Task<ProjectionSourceSnapshot> ReadAsync(
        string stateSpaceId,
        ApplicationIdentifier expectedOwner,
        IReadOnlyList<EcsComponentLocator> locators,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSpaceId);
        ArgumentNullException.ThrowIfNull(expectedOwner);
        ArgumentNullException.ThrowIfNull(locators);
        if (locators.Count > 256)
            throw new InvalidOperationException("Projection component read bound exceeded.");
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var stateSpace = stateSpaces.Get(stateSpaceId)
            ?? throw new InvalidOperationException("Unknown projection state space.");
        if (stateSpace.ApplicationRevision.ApplicationId != expectedOwner)
            throw new InvalidOperationException("A projection cannot cross an application state-space boundary.");
        var read = await components.GetComponentsAsync(stateSpaceId, locators, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(stateSpace, read);
    }
}
