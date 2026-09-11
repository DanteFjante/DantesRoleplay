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

/// <summary>Retains the deterministic compatibility/reuse review for an existing body update.</summary>
internal sealed class ApplicationCandidateCompatibleUpdateValidation(
    DantesRoleplayDbContext db, ApplicationCandidateCompatibleUpdateReader updates,
    IStandingGrantTargetResolver targets, IStandingGrantPolicy grants,
    IInteractionManualContextService manuals, IOperationLog operations)
{
    private const string Tool = "application-candidate-compatible-update";

    internal async Task<bool> CompleteAsync(InteractionInvocationHost host, ApplicationCandidateRuntimeReport report,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || report.Status != ApplicationCandidateRuntimeStatus.Completed)
            return false;
        var update = await updates.ReadAsync(host, report.Candidate, cancellationToken);
        if (update is null || report.SelectionEvidenceFingerprint != update.Closure.EvidenceFingerprint
            || report.RuntimePolicyVersion != ApplicationCandidateRuntimeValidator.RuntimePolicyVersion
            || report.RuntimePolicyFingerprint != ApplicationCandidateRuntimeValidator.RuntimePolicyFingerprint) return false;
        foreach (var previous in update.Predecessors)
        {
            var resolved = await targets.ResolveAsync(host, previous, cancellationToken);
            if (resolved.Target is null || resolved.Status != StandingGrantTargetResolutionStatus.Available) return false;
            var permission = await grants.EvaluateAsync(host,
                new(StandingGrantCapability.Read, StandingGrantScope.Application, [resolved.Target], []), cancellationToken);
            if (!permission.Allowed) return false;
        }
        var readHost = InteractionInvocationHost.ForApplication(host.Principal, host.ApplicationRevision,
            host.GrantReference, host.CommandId, InteractionExecutionProfile.ReadOnly, host.Budget, host.ParentCommandId);
        var discovery = await manuals.DiscoverAsync(new(readHost, update.Predecessors[0].DefinitionId), cancellationToken);
        if (discovery.Tag != InteractionInvocationResultTag.Completed || discovery.DataJson is null
            || !TryManualFingerprint(discovery.DataJson, out var manualHash)
            || discovery.CompletionEvidenceReference != "manual.context." + manualHash!.ToLowerInvariant()) return false;

        // This review does not infer semantic uniqueness from search hits. It proves there are no
        // new identities: the very same authored contracts are retained and their bodies updated.
        var review = new Review(report.Candidate, update.Fingerprint,
            update.Retained.RevisionRow.NewImplementationReason, update.Predecessors.ToArray(), discovery.DataJson);
        var json = Canonical(review);
        var operationId = ApplicationCandidateOperationProof.OperationId(host.Principal.PrincipalId,
            report.Candidate.ApplicationId.Value, host.CommandId, "compatible-update");
        var operation = await operations.RecordAsync(Tool, "Validated existing mechanic body updates and retained their manual context.",
            true, subject: report.Candidate.ApplicationId.Value, projectionJson: json,
            guardEvidenceJson: Canonical(new { principal = host.Principal.PrincipalId,
                host.Principal.AuthenticationMethod, host.GrantReference, update.Fingerprint }),
            id: operationId, cancellationToken: cancellationToken);
        var reference = operation.Id + ":" + Hash(json + "\0" + operation.GuardEvidenceJson);
        validation.DependenciesJson = Dependencies(update);
        validation.DependencyFingerprint = update.Fingerprint;
        validation.DependenciesComplete = true;
        validation.DependencyEvidenceReference = reference;
        validation.ReuseEvidenceReference = reference;
        validation.ManualPacketResultFingerprint = manualHash;
        validation.AlternativesJson = Canonical(update.Predecessors.Select(value => new ApplicationDefinitionAlternative(
            value.DefinitionId, value.Revision, value.ContentFingerprint, "Existing identity and contract retained; only its JavaScript body changes.")));
        validation.Outcome = "valid";
        validation.DiagnosticsJson = "[]";
        return true;
    }

    internal static async Task<bool> VerifyAsync(DantesRoleplayDbContext db,
        ApplicationCandidateCompatibleUpdateEvidence update, ApplicationCandidateValidationRecord validation,
        CancellationToken cancellationToken)
    {
        if (validation.Outcome != "valid" || !validation.DependenciesComplete
            || validation.DependencyFingerprint != update.Fingerprint || validation.DependenciesJson != Dependencies(update)
            || validation.ReuseEvidenceReference != validation.DependencyEvidenceReference
            || validation.ReuseEvidenceReference is not { Length: 97 } reference || reference[32] != ':') return false;
        var id = reference[..32];
        var operation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (operation is null || !operation.Success || operation.Tool != Tool
            || operation.Subject != update.Closure.Candidate.ApplicationId.Value
            || Hash(operation.ProjectionJson + "\0" + operation.GuardEvidenceJson) != reference[33..]) return false;
        try
        {
            using var document = JsonDocument.Parse(operation.ProjectionJson);
            if (!document.RootElement.TryGetProperty("ManualJson", out var manual)
                || manual.ValueKind != JsonValueKind.String || manual.GetString() is not { } manualJson) return false;
            var expected = new Review(update.Closure.Candidate, update.Fingerprint,
                update.Retained.RevisionRow.NewImplementationReason, update.Predecessors.ToArray(), manualJson);
            return Canonical(expected) == operation.ProjectionJson
                && TryManualFingerprint(manualJson, out var hash) && hash == validation.ManualPacketResultFingerprint;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        { return false; }
    }

    private static string Dependencies(ApplicationCandidateCompatibleUpdateEvidence update) => Canonical(
        update.Predecessors.Select(value => new ApplicationCandidateDependency(value.DefinitionId, value.Revision, value.ContentFingerprint)));
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
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
        { return false; }
    }

    private sealed record Review(ApplicationCandidateReference Candidate, string CompatibilityFingerprint,
        string Reason, StandingGrantDefinitionReference[] Predecessors, string ManualJson);
}
