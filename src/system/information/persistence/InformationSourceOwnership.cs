using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Reads the source owner's current immutable binding; textual information scopes convey no ownership.</summary>
internal static class InformationSourceOwnership
{
    internal static async Task<InformationSourceOwnerRevisionRecord> AppendAsync(DantesRoleplayDbContext db,
        string sourceId, string applicationId, string qualifiedTargetId, string operationId,
        InformationSourceOwnerReadback? prior, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Ownership requires its command transaction.");
        var identity = await db.Set<InformationSourceTargetIdentityRecord>().SingleOrDefaultAsync(
            value => value.QualifiedTargetId == qualifiedTargetId, cancellationToken);
        if (identity is not null && identity.SourceId != sourceId)
            throw new InvalidOperationException("The qualified information target is permanently assigned to another source.");
        if (identity is null) db.Add(new InformationSourceTargetIdentityRecord
        {
            QualifiedTargetId = qualifiedTargetId, SourceId = sourceId, CreatedByOperationId = operationId
        });
        var next = new InformationSourceOwnerRevisionRecord
        {
            SourceId = sourceId, ApplicationId = applicationId, QualifiedTargetId = qualifiedTargetId,
            Revision = checked((prior?.Owner.Revision ?? 0) + 1), PreviousFingerprint = prior?.Owner.ContentFingerprint,
            BoundByOperationId = operationId, ContentFingerprint = string.Empty
        };
        next.ContentFingerprint = Fingerprint(next);
        db.Add(next);
        await db.SaveChangesAsync(cancellationToken);
        var current = await db.Set<InformationSourceOwnerCurrentRecord>().SingleOrDefaultAsync(
            value => value.SourceId == sourceId, cancellationToken);
        if (current is null) db.Add(new InformationSourceOwnerCurrentRecord
        {
            SourceId = sourceId, Revision = next.Revision, QualifiedTargetId = qualifiedTargetId
        });
        else { current.Revision = next.Revision; current.QualifiedTargetId = qualifiedTargetId; }
        await db.SaveChangesAsync(cancellationToken);
        return next;
    }

    internal static string Fingerprint(InformationSourceOwnerRevisionRecord value) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/information-source-owner/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                value.SourceId, value.Revision, value.ApplicationId, value.QualifiedTargetId,
                value.PreviousFingerprint, value.BoundByOperationId
            })));

    internal static async Task<InformationSourceOwnerReadback?> ReadAsync(DantesRoleplayDbContext db,
        string sourceId, CancellationToken cancellationToken)
    {
        var current = await db.Set<InformationSourceOwnerCurrentRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.SourceId == sourceId, cancellationToken);
        if (current is null) return null;
        var owner = await db.Set<InformationSourceOwnerRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.SourceId == sourceId && value.Revision == current.Revision,
                cancellationToken);
        if (owner is null || owner.QualifiedTargetId != current.QualifiedTargetId
            || owner.ContentFingerprint != Fingerprint(owner)) return null;
        var identity = await db.Set<InformationSourceTargetIdentityRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.QualifiedTargetId == current.QualifiedTargetId, cancellationToken);
        if (identity?.SourceId != sourceId) return null;
        var source = await db.Set<InformationSource>().AsNoTracking()
            .Where(value => value.Id == sourceId && value.Name.Length <= 200 && value.Description.Length <= 1000
                && value.MetadataSchemaJson.Length <= 8000 && value.ScopeId.Length <= 200)
            .SingleOrDefaultAsync(cancellationToken);
        if (source is null || source.Revision < 1 || source.ContentHash != ContentHash.Of(source.Id,
                source.ScopeId, source.Name, source.Description, source.MetadataSchemaJson)) return null;
        return new(owner, source);
    }
}

internal sealed record InformationSourceOwnerReadback(InformationSourceOwnerRevisionRecord Owner, InformationSource Source);
