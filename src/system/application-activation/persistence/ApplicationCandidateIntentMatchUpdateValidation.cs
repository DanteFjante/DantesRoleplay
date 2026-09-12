using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Retains exact structural and current-authority evidence for a Matches-only update.</summary>
internal sealed class ApplicationCandidateIntentMatchUpdateValidation(
    DantesRoleplayDbContext db, IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants, IInteractionManualContextService manuals, IOperationLog operations)
{
    internal const string PreparationVersion = "intent-match-only-v1";
    private const string Tool = "application-candidate-intent-match-update";

    internal async Task<bool> CompleteAsync(InteractionInvocationHost host,
        ApplicationCandidateIntentMatchUpdateEvidence update,
        ApplicationCandidateValidationRecord validation,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) return false;
        var resolved = await targets.ResolveAsync(host, update.Predecessor, cancellationToken);
        if (resolved.Status != StandingGrantTargetResolutionStatus.Available
            || resolved.Target is not { } target || resolved.CurrentActivation is not { } origin
            || origin.ActivationRevision != update.Basis.ActivationRevision
            || origin.ActivationFingerprint != update.Basis.ActivationFingerprint)
            return false;
        var permission = await grants.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, [target], []), cancellationToken);
        if (!permission.Allowed) return false;

        var readHost = InteractionInvocationHost.ForApplication(host.Principal, host.ApplicationRevision,
            host.GrantReference, host.CommandId, InteractionExecutionProfile.ReadOnly,
            host.Budget, host.ParentCommandId);
        var discovery = await manuals.DiscoverAsync(
            new(readHost, update.Predecessor.DefinitionId), cancellationToken);
        if (discovery.Tag != InteractionInvocationResultTag.Completed || discovery.DataJson is null
            || !TryManualFingerprint(discovery.DataJson, out var manualHash)
            || discovery.CompletionEvidenceReference != "manual.context." + manualHash!.ToLowerInvariant())
            return false;

        var review = new Review(update.Candidate, update.Basis.ActivationFingerprint,
            update.Fingerprint, update.Retained.RevisionRow.NewImplementationReason,
            update.Predecessor, update.Successor, update.SuccessorRecord.MatchPhrases.ToArray(),
            discovery.DataJson);
        var json = Canonical(review);
        var operationId = ApplicationCandidateOperationProof.OperationId(host.Principal.PrincipalId,
            update.Candidate.ApplicationId.Value, host.CommandId, "intent-match-update");
        var operation = await operations.RecordAsync(Tool,
            "Validated an exact Matches-only update to an existing definition.", true,
            subject: update.Candidate.ApplicationId.Value, projectionJson: json,
            guardEvidenceJson: Canonical(new
            {
                principal = host.Principal.PrincipalId,
                host.Principal.AuthenticationMethod,
                host.GrantReference,
                update.Fingerprint
            }), id: operationId, cancellationToken: cancellationToken);
        var reference = operation.Id + ":" + Hash(json + "\0" + operation.GuardEvidenceJson);
        validation.DependencyFingerprint = update.Fingerprint;
        validation.DependenciesJson = Canonical(new[]
        {
            new ApplicationCandidateDependency(update.Predecessor.DefinitionId,
                update.Predecessor.Revision, update.Predecessor.ContentFingerprint)
        });
        validation.DependenciesComplete = true;
        validation.DependencyEvidenceReference = reference;
        validation.PreparedEvidenceReference = reference;
        validation.ReuseEvidenceReference = reference;
        validation.PreparationVersion = PreparationVersion;
        validation.ManualPacketResultFingerprint = manualHash;
        validation.AlternativesJson = Canonical(new[]
        {
            new ApplicationDefinitionAlternative(update.Predecessor.DefinitionId,
                update.Predecessor.Revision, update.Predecessor.ContentFingerprint,
                "The same existing definition is retained; only explicit match phrases change.")
        });
        validation.Outcome = "valid";
        validation.DiagnosticsJson = "[]";
        return true;
    }

    internal static async Task<bool> VerifyAsync(DantesRoleplayDbContext db,
        ApplicationCandidateIntentMatchUpdateEvidence update,
        ApplicationCandidateValidationRecord validation,
        CancellationToken cancellationToken)
    {
        if (validation.Outcome != "valid" || !validation.DependenciesComplete
            || validation.PreparationVersion != PreparationVersion
            || validation.DependencyFingerprint != update.Fingerprint
            || validation.DependenciesJson != Canonical(new[]
            {
                new ApplicationCandidateDependency(update.Predecessor.DefinitionId,
                    update.Predecessor.Revision, update.Predecessor.ContentFingerprint)
            })
            || validation.PreparedEvidenceReference != validation.DependencyEvidenceReference
            || validation.ReuseEvidenceReference != validation.DependencyEvidenceReference
            || validation.ReuseEvidenceReference is not { Length: 97 } reference || reference[32] != ':')
            return false;
        var operationId = reference[..32];
        var operation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == operationId, cancellationToken);
        if (operation is null || !operation.Success || operation.Tool != Tool
            || operation.Subject != update.Candidate.ApplicationId.Value
            || Hash(operation.ProjectionJson + "\0" + operation.GuardEvidenceJson) != reference[33..])
            return false;
        try
        {
            using var document = JsonDocument.Parse(operation.ProjectionJson);
            if (!document.RootElement.TryGetProperty("ManualJson", out var manual)
                || manual.ValueKind != JsonValueKind.String
                || manual.GetString() is not { } manualJson) return false;
            var expected = new Review(update.Candidate, update.Basis.ActivationFingerprint,
                update.Fingerprint, update.Retained.RevisionRow.NewImplementationReason,
                update.Predecessor, update.Successor, update.SuccessorRecord.MatchPhrases.ToArray(),
                manualJson);
            return Canonical(expected) == operation.ProjectionJson
                && TryManualFingerprint(manualJson, out var hash)
                && hash == validation.ManualPacketResultFingerprint;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or ArgumentException or NullReferenceException)
        {
            return false;
        }
    }

    private static string Canonical<T>(T value) =>
        InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(value));
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
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
            or ArgumentException or NullReferenceException)
        {
            return false;
        }
    }

    private sealed record Review(ApplicationCandidateReference Candidate,
        string BasisActivationFingerprint, string MatchUpdateFingerprint, string Reason,
        StandingGrantDefinitionReference Predecessor,
        StandingGrantDefinitionReference Successor,
        IReadOnlyList<string> MatchPhrases, string ManualJson);
}
