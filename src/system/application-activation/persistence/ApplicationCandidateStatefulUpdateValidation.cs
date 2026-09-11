using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

internal sealed class ApplicationCandidateStatefulUpdateValidation(
    DantesRoleplayDbContext db, IStandingGrantTargetResolver targets, IStandingGrantPolicy grants,
    IInteractionManualContextService manuals, IOperationLog operations,
    ApplicationCandidateStatefulRuntimeValidator runtime)
{
    private const string Tool = "application-candidate-stateful-update";

    internal async Task<bool> CompleteAsync(InteractionInvocationHost host,
        ApplicationCandidateStatefulUpdateEvidence update, ApplicationCandidateStatefulRuntimeReport report,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null
            || !await runtime.CurrentAsync(update, report, host, cancellationToken)) return false;
        var resolved = await targets.ResolveAsync(host, update.PredecessorDefinition, cancellationToken);
        if (resolved is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }) return false;
        var read = await grants.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, [target], []), cancellationToken);
        if (!read.Allowed) return false;
        var readHost = InteractionInvocationHost.ForApplication(host.Principal, host.ApplicationRevision,
            host.GrantReference, host.CommandId, InteractionExecutionProfile.ReadOnly, host.Budget, host.ParentCommandId);
        var discovery = await manuals.DiscoverAsync(new(readHost, update.PredecessorDefinition.DefinitionId), cancellationToken);
        if (discovery.Tag != InteractionInvocationResultTag.Completed || discovery.DataJson is null
            || !TryManualFingerprint(discovery.DataJson, out var manualHash)
            || discovery.CompletionEvidenceReference != "manual.context." + manualHash!.ToLowerInvariant()) return false;
        var review = new Review(update.Candidate, update.Fingerprint,
            update.Retained.RevisionRow.NewImplementationReason, update.PredecessorDefinition,
            report.Dependencies, report.StateGrantReference, report.StateGrantFingerprint,
            report.EffectKinds, discovery.DataJson);
        var json = Canonical(review);
        var operationId = ApplicationCandidateOperationProof.OperationId(host.Principal.PrincipalId,
            update.Candidate.ApplicationId.Value, host.CommandId, "stateful-update");
        var operation = await operations.RecordAsync(Tool,
            "Validated one existing stateful atomic mechanic body and its retained manual context.", true,
            subject: update.Candidate.ApplicationId.Value, projectionJson: json,
            guardEvidenceJson: Canonical(new { principal = host.Principal.PrincipalId,
                host.Principal.AuthenticationMethod, host.GrantReference, update.Fingerprint,
                report.StateGrantReference, report.StateGrantFingerprint, report.EffectKinds }),
            id: operationId, cancellationToken: cancellationToken);
        var reference = operation.Id + ":" + Hash(json + "\0" + operation.GuardEvidenceJson);
        validation.DependenciesJson = Canonical(report.Dependencies.Select(value =>
            new ApplicationCandidateDependency(value.DefinitionId, value.Revision, value.ContentFingerprint)));
        validation.DependencyFingerprint = update.Fingerprint;
        validation.DependenciesComplete = true;
        validation.DependencyEvidenceReference = reference;
        validation.ReuseEvidenceReference = reference;
        validation.ManualPacketResultFingerprint = manualHash;
        validation.AlternativesJson = Canonical(new[] { new ApplicationDefinitionAlternative(
            update.PredecessorDefinition.DefinitionId, update.PredecessorDefinition.Revision,
            update.PredecessorDefinition.ContentFingerprint,
            "Existing atomic identity and contract retained; only its JavaScript body changes.") });
        validation.Outcome = "valid";
        validation.DiagnosticsJson = "[]";
        return true;
    }

    internal static async Task<bool> VerifyAsync(DantesRoleplayDbContext db,
        ApplicationCandidateStatefulUpdateEvidence update, ApplicationCandidateStatefulRuntimeReport report,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        var dependencies = Canonical(report.Dependencies.Select(value =>
            new ApplicationCandidateDependency(value.DefinitionId, value.Revision, value.ContentFingerprint)));
        if (validation.Outcome != "valid" || !validation.DependenciesComplete
            || validation.DependencyFingerprint != update.Fingerprint || validation.DependenciesJson != dependencies
            || validation.ReuseEvidenceReference != validation.DependencyEvidenceReference
            || validation.ReuseEvidenceReference is not { Length: 97 } reference || reference[32] != ':') return false;
        var operationId = reference[..32];
        var operation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == operationId, cancellationToken);
        if (operation is null || !operation.Success || operation.Tool != Tool
            || operation.Subject != update.Candidate.ApplicationId.Value
            || Hash(operation.ProjectionJson + "\0" + operation.GuardEvidenceJson) != reference[33..]) return false;
        try
        {
            using var document = JsonDocument.Parse(operation.ProjectionJson);
            if (!document.RootElement.TryGetProperty("ManualJson", out var manual)
                || manual.ValueKind != JsonValueKind.String || manual.GetString() is not { } manualJson) return false;
            var expected = new Review(update.Candidate, update.Fingerprint,
                update.Retained.RevisionRow.NewImplementationReason, update.PredecessorDefinition,
                report.Dependencies, report.StateGrantReference, report.StateGrantFingerprint,
                report.EffectKinds, manualJson);
            return Canonical(expected) == operation.ProjectionJson
                && TryManualFingerprint(manualJson, out var hash)
                && hash == validation.ManualPacketResultFingerprint;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        { return false; }
    }

    private static string Canonical<T>(T value) => InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(value));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool TryManualFingerprint(string json, out string? fingerprint)
    {
        fingerprint = null;
        if (Encoding.UTF8.GetByteCount(json) > 96_000) return false;
        try
        {
            var packet = JsonNode.Parse(json)!.AsObject();
            fingerprint = packet["resultFingerprint"]!.GetValue<string>();
            packet["resultFingerprint"] = new string('0', 64);
            return fingerprint is { Length: 64 }
                && Hash(InteractionCanonicalJson.CanonicalizeObject(packet.ToJsonString())) == fingerprint;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or ArgumentException or NullReferenceException) { return false; }
    }

    private sealed record Review(ApplicationCandidateReference Candidate, string CompatibilityFingerprint,
        string Reason, StandingGrantDefinitionReference Predecessor,
        IReadOnlyList<StandingGrantDefinitionReference> Dependencies,
        string StateGrantReference, string StateGrantFingerprint,
        IReadOnlyList<string> EffectKinds, string ManualJson);
}
