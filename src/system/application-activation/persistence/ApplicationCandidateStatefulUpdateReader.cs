using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Proves one existing atomic mechanic body replacement with an unchanged retained contract.</summary>
internal sealed class ApplicationCandidateStatefulUpdateReader(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence, IStandingGrantTargetResolver targets)
{
    internal async Task<ApplicationCandidateStatefulUpdateEvidence?> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var retained = await new ApplicationCandidateRetainedReader(db, applications)
                .ReadMetadataAsync(candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
            if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint) return null;
            var basis = await ApplicationCandidateDocumentSelection.ReadBaseAsync(db, activations, candidate.ApplicationId,
                retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
            if (basis?.PreparationVersion is null || basis.Winners.Count != retained.Documents.Count
                || basis.ApplicationRevision != retained.ApplicationRevision.Revision
                || basis.ApplicationFingerprint != retained.ApplicationRevision.Fingerprint) return null;
            var selection = await new ApplicationCandidateSelectionReader(db, applications, activations, targets)
                .ReadAsync(host, candidate, cancellationToken);
            if (selection is null || selection.BaseOrigin?.ActivationFingerprint != basis.ActivationFingerprint
                || selection.Targets.Length != 1 || selection.Documents.Length != 2
                || selection.ChangedPaths.Length != 1) return null;
            var target = selection.Targets[0];
            if (target.Kind != "mechanic") return null;
            var markdown = selection.Documents.SingleOrDefault(value =>
                value.Document.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
            var javascript = selection.Documents.SingleOrDefault(value =>
                value.Document.RelativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
            if (markdown is null || javascript is null
                || selection.ChangedPaths[0] != javascript.Document.RelativePath
                || Path.ChangeExtension(markdown.Document.RelativePath, ".js").Replace('\\', '/') != javascript.Document.RelativePath)
                return null;
            var originals = basis.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
            if (!originals.TryGetValue(markdown.Document.RelativePath, out var oldMarkdown)
                || oldMarkdown != markdown.Document
                || !originals.TryGetValue(javascript.Document.RelativePath, out var oldJavaScript)
                || oldJavaScript == javascript.Document) return null;
            foreach (var document in retained.Documents)
            {
                if (!originals.TryGetValue(document.RelativePath, out var original)) return null;
                if (document.RelativePath == javascript.Document.RelativePath)
                {
                    if (!ApplicationCandidateDocumentSelection.SameSourceMediaType(document, original)
                        || document with { ContentFingerprint = original.ContentFingerprint, Length = original.Length,
                            MediaType = original.MediaType } != original) return null;
                }
                else if (document != original) return null;
            }
            var oldEvidence = evidence.ReadDocumentEvidence(candidate.ApplicationId, basis.ActivationRevision,
                oldJavaScript.LogicalIdentity);
            if (oldEvidence?.RetainedBytes is not { } oldBytes || oldEvidence.IsLegacyMetadataOnly
                || oldEvidence.ContentFingerprint != oldJavaScript.ContentFingerprint
                || oldEvidence.Length != oldJavaScript.Length) return null;
            var candidateDocuments = new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
            {
                [markdown.Document.RelativePath] = markdown.Document,
                [javascript.Document.RelativePath] = javascript.Document
            };
            var candidateBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [markdown.Document.RelativePath] = markdown.RetainedBytes.ToArray(),
                [javascript.Document.RelativePath] = javascript.RetainedBytes.ToArray()
            };
            var successorDefinition = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
                markdown.Document, candidateDocuments, candidateBytes);
            var predecessorDefinition = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
                oldMarkdown, new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
                { [oldMarkdown.RelativePath] = oldMarkdown, [oldJavaScript.RelativePath] = oldJavaScript },
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { [oldMarkdown.RelativePath] = markdown.RetainedBytes.ToArray(), [oldJavaScript.RelativePath] = oldBytes });
            if (successorDefinition is null || predecessorDefinition is null || successorDefinition.Kind != "mechanic"
                || successorDefinition.QualifiedId != target.DefinitionId || successorDefinition.Version != target.Revision
                || successorDefinition.ContentFingerprint != target.ContentFingerprint
                || predecessorDefinition.Kind != successorDefinition.Kind
                || predecessorDefinition.QualifiedId != successorDefinition.QualifiedId
                || predecessorDefinition.Version != successorDefinition.Version || successorDefinition.Status != "active") return null;
            var successor = View(successorDefinition);
            var predecessor = View(predecessorDefinition);
            var requirements = Requirements(successor);
            if (requirements is null || requirements.Event is not null || ServiceRequirement(successor)
                || !Stateful(requirements)) return null;
            var source = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == retained.RevisionRow.SourceOperationId, cancellationToken);
            var definition = new StandingGrantDefinitionReference(successor.Summary.QualifiedId, successor.Summary.Kind,
                successor.Summary.Version, successor.Summary.ContentFingerprint);
            if (source is null || !ApplicationCandidateOperationProof.WriteMatches(source, retained, [definition], out _))
                return null;
            return ApplicationCandidateStatefulUpdateEvidence.Create(retained, basis, successor, predecessor,
                requirements, definition, new(predecessor.Summary.QualifiedId, predecessor.Summary.Kind,
                    predecessor.Summary.Version, predecessor.Summary.ContentFingerprint));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or ApplicationActivationException or ApplicationCatalogMaterializationException)
        { return null; }
    }

    private static MechanicRequirements? Requirements(CatalogRecordView record)
    {
        using var document = JsonDocument.Parse(record.ContentJson);
        return document.RootElement.TryGetProperty("requirements", out var value)
            && value.ValueKind == JsonValueKind.String ? MechanicRequirements.Parse(value.GetString()!) : null;
    }

    private static bool ServiceRequirement(CatalogRecordView record)
    {
        using var document = JsonDocument.Parse(record.ContentJson);
        using var requirements = JsonDocument.Parse(document.RootElement.GetProperty("requirements").GetString()!);
        return requirements.RootElement.EnumerateObject().Any(value =>
            value.Name.Equals("service", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Stateful(MechanicRequirements requirements) => requirements.Roles.Count > 0
        || requirements.ObjectRoles.Count > 0 || requirements.SnapshotObjects.Count > 0
        || requirements.GraphSnapshots.Count > 0 || requirements.AuthorizedContext is not null
        || requirements.Children.Count > 0 || requirements.EffectComponentIds.Count > 0
        || requirements.ElapsedTime is not null;

    private static CatalogRecordView View(CatalogRecordDefinition record) => new(new(
        record.Collection, record.Kind, record.QualifiedId, record.Name, record.Description,
        record.Path, record.Status, record.Version, record.ContentFingerprint,
        record.SourceId, record.SourceLogicalPath), record.ContentJson);
}

internal sealed class ApplicationCandidateStatefulUpdateEvidence
{
    private ApplicationCandidateStatefulUpdateEvidence(ApplicationCandidateRetainedMetadata retained,
        ActiveApplicationManifest basis, CatalogRecordView successor, CatalogRecordView predecessor,
        MechanicRequirements requirements, StandingGrantDefinitionReference definition,
        StandingGrantDefinitionReference predecessorDefinition)
    {
        Retained = retained; Basis = basis; Successor = successor; Predecessor = predecessor;
        Requirements = requirements; Definition = definition; PredecessorDefinition = predecessorDefinition;
        Fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/stateful-atomic-body-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                candidate = new ApplicationCandidateReference(retained.ApplicationRevision.ApplicationId,
                    retained.RevisionRow.CandidateId, retained.RevisionRow.Revision,
                    retained.RevisionRow.ContentFingerprint),
                basis = basis.ActivationFingerprint, definition, predecessorDefinition,
                successor = successor.Summary, predecessor = predecessor.Summary
            })));
    }

    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal CatalogRecordView Successor { get; }
    internal CatalogRecordView Predecessor { get; }
    internal MechanicRequirements Requirements { get; }
    internal StandingGrantDefinitionReference Definition { get; }
    internal StandingGrantDefinitionReference PredecessorDefinition { get; }
    internal ApplicationCandidateReference Candidate => new(Retained.ApplicationRevision.ApplicationId,
        Retained.RevisionRow.CandidateId, Retained.RevisionRow.Revision, Retained.RevisionRow.ContentFingerprint);
    internal string Fingerprint { get; }

    internal static ApplicationCandidateStatefulUpdateEvidence Create(ApplicationCandidateRetainedMetadata retained,
        ActiveApplicationManifest basis, CatalogRecordView successor, CatalogRecordView predecessor,
        MechanicRequirements requirements, StandingGrantDefinitionReference definition,
        StandingGrantDefinitionReference predecessorDefinition) =>
        new(retained, basis, successor, predecessor, requirements, definition, predecessorDefinition);
}
