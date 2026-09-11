using System.Collections.Immutable;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Issues evidence only for the exhaustive selected pure-mechanic profile, never global catalog completeness.</summary>
internal sealed class ApplicationCandidatePureRuntimeClosureReader(DantesRoleplayDbContext db, IApplicationRegistry applications,
    IApplicationActivationReader activations, IStandingGrantTargetResolver targets, ApplicationCandidatePureMechanicClassifier classifier)
{
    internal Task<ApplicationCandidatePureRuntimeClosureEvidence?> ReadAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken = default) =>
        ApplicationCandidatePureRuntimeClosureEvidence.ReadAsync(db, applications, activations, targets, classifier,
            host, candidate, cancellationToken);
}

internal sealed record ApplicationCandidatePureRuntimeDefinition(ApplicationCandidatePureMechanicPlan Plan,
    ApplicationCandidateSelectedDocument Markdown, ApplicationCandidateSelectedDocument JavaScript);

internal sealed class ApplicationCandidatePureRuntimeClosureEvidence
{
    private ApplicationCandidatePureRuntimeClosureEvidence(ApplicationCandidateSelectionEvidence selection,
        ImmutableArray<ApplicationCandidatePureRuntimeDefinition> definitions)
    {
        Candidate = selection.Candidate; BaseOrigin = selection.BaseOrigin;
        SelectionEvidenceFingerprint = selection.EvidenceFingerprint; Definitions = definitions;
        EvidenceFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-pure-runtime-closure/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Candidate, BaseOrigin, SelectionEvidenceFingerprint, CoverageVersion,
                definitions = definitions.Select(value => new
                {
                    value.Plan.Definition, value.Plan.ClassificationFingerprint,
                    markdown = value.Markdown.Document, javascript = value.JavaScript.Document
                })
            })));
    }

    internal ApplicationCandidateReference Candidate { get; }
    internal StandingGrantActivationOrigin? BaseOrigin { get; }
    internal string SelectionEvidenceFingerprint { get; }
    internal string CoverageVersion => "selected-pure-mechanic-runtime-v1";
    internal string EvidenceFingerprint { get; }
    internal ImmutableArray<ApplicationCandidatePureRuntimeDefinition> Definitions { get; }

    // There is intentionally no constructor/factory taking a caller's completeness flag or a list
    // of alleged plans. Every instance passes the actual retained reader and closed grammar owner.
    internal static async Task<ApplicationCandidatePureRuntimeClosureEvidence?> ReadAsync(DantesRoleplayDbContext db,
        IApplicationRegistry applications, IApplicationActivationReader activations, IStandingGrantTargetResolver targets,
        ApplicationCandidatePureMechanicClassifier classifier, InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var selection = await new ApplicationCandidateSelectionReader(db, applications, activations, targets)
                .ReadAsync(host, candidate, cancellationToken);
            if (selection is null || selection.Targets.Length == 0 || selection.Targets.Any(value => value.Kind != "mechanic")) return null;
            var winners = selection.Documents.ToDictionary(value => value.Document.RelativePath, value => value.Document, StringComparer.Ordinal);
            var bytes = selection.Documents.ToDictionary(value => value.Document.RelativePath, value => value.RetainedBytes.ToArray(), StringComparer.Ordinal);
            var accounted = new HashSet<string>(StringComparer.Ordinal);
            var definitions = ImmutableArray.CreateBuilder<ApplicationCandidatePureRuntimeDefinition>();
            foreach (var target in selection.Targets.OrderBy(value => value.DefinitionId, StringComparer.Ordinal))
            {
                var reference = new StandingGrantDefinitionReference(target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint);
                var pair = selection.Documents.Where(value => value.Definition == reference).ToArray();
                if (pair.Length != 2) return null;
                var markdown = pair.SingleOrDefault(value => value.Document.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                var javascript = pair.SingleOrDefault(value => value.Document.RelativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
                if (markdown is null || javascript is null
                    || Path.ChangeExtension(markdown.Document.RelativePath, ".js").Replace('\\', '/') != javascript.Document.RelativePath
                    || markdown.Document.SourceId != javascript.Document.SourceId || markdown.Document.Trust != javascript.Document.Trust
                    || markdown.Document.Precedence != javascript.Document.Precedence
                    || !accounted.Add(markdown.Document.RelativePath) || !accounted.Add(javascript.Document.RelativePath)) return null;
                var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
                    markdown.Document, winners, bytes);
                if (record is null || record.Kind != reference.Kind || record.QualifiedId != reference.DefinitionId
                    || record.Version != reference.Revision || record.ContentFingerprint != reference.ContentFingerprint
                    || record.SourceId != markdown.Document.SourceId || record.SourceLogicalPath != markdown.Document.RelativePath) return null;
                var summary = new CatalogRecordSummary(record.Collection, record.Kind, record.QualifiedId, record.Name,
                    record.Description, record.Path, record.Status, record.Version, record.ContentFingerprint, record.SourceId, record.SourceLogicalPath);
                var classification = classifier.Classify(new(summary, record.ContentJson));
                if (classification.Outcome != ApplicationCandidatePureMechanicOutcome.Supported || classification.Plan is not { } plan
                    || plan.Definition != reference) return null;
                definitions.Add(new(plan, markdown, javascript));
            }
            if (accounted.Count != selection.Documents.Length || selection.ChangedPaths.Any(path => !accounted.Contains(path))) return null;
            return new(selection, definitions.ToImmutable());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or ApplicationActivationException or ApplicationCatalogMaterializationException)
        { return null; }
    }
}
