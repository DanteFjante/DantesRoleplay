using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Proves the narrow reviewed publication case for new or contract-changed pure mechanics. The
/// model judgment is one checked input; retained task, accounting, source, runtime and current
/// authority evidence remain independently required.
/// </summary>
internal sealed class ApplicationCandidateReviewedPureUpdateReader(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence, IStandingGrantTargetResolver targets,
    SystemTaskApplicationValidationGate validationGate)
{
    internal async Task<ApplicationCandidateReviewedPureUpdateEvidence?> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction() is not SqliteTransaction transaction
            || db.Database.GetDbConnection() is not SqliteConnection connection) return null;
        try
        {
            var readHost = InteractionInvocationHost.ForApplication(host.Principal, host.ApplicationRevision,
                host.GrantReference, host.CommandId, InteractionExecutionProfile.ReadOnly, host.Budget, host.ParentCommandId);
            var authority = await validationGate.CheckAsync(readHost, candidate, true,
                cancellationToken: cancellationToken);
            if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is not null) return null;
            var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
                candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
            var basis = retained is null ? null : await ApplicationCandidateDocumentSelection.ReadBaseAsync(
                db, activations, candidate.ApplicationId, retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
            if (retained is null || basis is null || authority.PureClosure is null
                || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint
                || authority.PureClosure.BaseOrigin?.ActivationFingerprint != basis.ActivationFingerprint) return null;
            var source = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == retained.RevisionRow.SourceOperationId, cancellationToken);
            var definitions = authority.PureClosure.Definitions.Select(value => value.Plan.Definition).ToArray();
            if (source is null || !ApplicationCandidateOperationProof.WriteMatches(source, retained, definitions, out var original)
                || original is null) return null;
            var structure = await StructureAsync(readHost, retained, basis, authority.PureClosure, cancellationToken);
            if (structure is null) return null;

            var handles = new List<SystemTaskDurableHandle>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT task_id,command_id FROM system_task_lifecycle
                    WHERE purpose='application-validation' AND state='completed'
                      AND application_id=$application AND candidate_id=$candidate
                      AND candidate_revision=$revision AND candidate_fingerprint=$fingerprint
                      AND principal_reference=$principal
                    ORDER BY completed_at_utc DESC,task_id DESC LIMIT 17
                    """;
                command.Parameters.AddWithValue("$application", candidate.ApplicationId.Value);
                command.Parameters.AddWithValue("$candidate", candidate.CandidateId);
                command.Parameters.AddWithValue("$revision", candidate.Revision);
                command.Parameters.AddWithValue("$fingerprint", candidate.ContentFingerprint);
                command.Parameters.AddWithValue("$principal", host.Principal.PrincipalId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) handles.Add(new(reader.GetString(0), reader.GetString(1)));
            }
            if (handles.Count > 16) return null;
            var store = new SqliteSystemTaskLifecycleStore(connection.ConnectionString, TimeProvider.System);
            foreach (var handle in handles)
            {
                var proof = await store.ReadCompletedValidationProofAsync(handle, connection, transaction, cancellationToken);
                if (proof is null || !TaskScopeMatches(proof, host, candidate, retained, original.CommandId)) continue;
                if (!TryReadJudgment(proof, authority, out var judgment) || judgment is null) continue;
                if (!JudgmentSupports(structure.Value, judgment)) continue;
                return ApplicationCandidateReviewedPureUpdateEvidence.FromVerified(
                    retained, basis, authority.PureClosure, structure.Value.Predecessors,
                    proof, judgment, authority.ReviewInput!, original.CommandId);
            }
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or JsonException or InteractionContractException or ApplicationActivationException
            or SystemTaskException or CryptographicException or InvalidDataException)
        { return null; }
    }

    private async Task<(ImmutableArray<StandingGrantDefinitionReference> Predecessors, bool HasNew)?> StructureAsync(
        InteractionInvocationHost host, ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidatePureRuntimeClosureEvidence closure, CancellationToken cancellationToken)
    {
        var active = activations.Current(closure.Candidate.ApplicationId);
        var basisIsCurrent = active?.ActivationFingerprint == basis.ActivationFingerprint;
        var candidateIsCurrent = active?.CandidateManifestFingerprint == closure.Candidate.ContentFingerprint
            && active.Winners.SequenceEqual(retained.Documents);
        if (!basisIsCurrent && !candidateIsCurrent) return null;
        var current = retained.Documents.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var previous = basis.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var selected = closure.Definitions.SelectMany(value => new[]
            { value.Markdown.Document.RelativePath, value.JavaScript.Document.RelativePath }).ToHashSet(StringComparer.Ordinal);
        if (basis.Winners.Any(value => !current.ContainsKey(value.RelativePath))
            || retained.Documents.Any(value => !previous.TryGetValue(value.RelativePath, out var old)
                ? !selected.Contains(value.RelativePath)
                : value != old && !selected.Contains(value.RelativePath))) return null;
        var predecessors = ImmutableArray.CreateBuilder<StandingGrantDefinitionReference>();
        var broader = false;
        var hasNew = false;
        foreach (var definition in closure.Definitions)
        {
            var markdownPath = definition.Markdown.Document.RelativePath;
            var javascriptPath = definition.JavaScript.Document.RelativePath;
            if (!previous.TryGetValue(markdownPath, out var oldMarkdown))
            {
                if (previous.ContainsKey(javascriptPath)) return null;
                if (basisIsCurrent)
                {
                    var resolved = await targets.ResolveCurrentAsync(host, definition.Plan.Definition.DefinitionId,
                        definition.Plan.Definition.Kind, cancellationToken);
                    if (resolved.Code != "STANDING_GRANT_DEFINITION_UNAVAILABLE") return null;
                }
                broader = true;
                hasNew = true;
                continue;
            }
            if (!previous.TryGetValue(javascriptPath, out var oldJavaScript)) return null;
            var markdownEvidence = evidence.ReadDocumentEvidence(closure.Candidate.ApplicationId,
                basis.ActivationRevision, oldMarkdown.LogicalIdentity);
            var javascriptEvidence = evidence.ReadDocumentEvidence(closure.Candidate.ApplicationId,
                basis.ActivationRevision, oldJavaScript.LogicalIdentity);
            if (markdownEvidence?.RetainedBytes is not { } markdownBytes || markdownEvidence.IsLegacyMetadataOnly
                || javascriptEvidence?.RetainedBytes is not { } javascriptBytes || javascriptEvidence.IsLegacyMetadataOnly)
                return null;
            var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(closure.Candidate.ApplicationId,
                oldMarkdown, new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
                { [markdownPath] = oldMarkdown, [javascriptPath] = oldJavaScript },
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { [markdownPath] = markdownBytes, [javascriptPath] = javascriptBytes });
            if (record is null || record.Kind != "mechanic"
                || record.QualifiedId != definition.Plan.Definition.DefinitionId) return null;
            var predecessor = new StandingGrantDefinitionReference(record.QualifiedId, record.Kind,
                record.Version, record.ContentFingerprint);
            if (basisIsCurrent)
            {
                var resolved = await targets.ResolveAsync(host, predecessor, cancellationToken);
                if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is null
                    || resolved.Target.OwnerApplicationId != closure.Candidate.ApplicationId) return null;
            }
            predecessors.Add(predecessor);
            broader |= oldMarkdown != definition.Markdown.Document;
        }
        return broader ? (predecessors.ToImmutable(), hasNew) : null;
    }

    internal static bool TaskScopeMatches(SystemTaskCompletedValidationProof proof, InteractionInvocationHost host,
        ApplicationCandidateReference candidate, ApplicationCandidateRetainedMetadata retained, string causalCommand)
    {
        var invocation = proof.Snapshot.Request.Invocation;
        return proof.Snapshot.Request.Candidate == candidate && proof.Snapshot.Request.Dependencies.Count == 0
            && proof.Snapshot.Request.Causation is { } cause
            && cause.OperationId == retained.RevisionRow.SourceOperationId && cause.CausalCommandId == causalCommand
            && invocation.PrincipalReference == host.Principal.PrincipalId
            && invocation.AuthenticationMethod == host.Principal.AuthenticationMethod
            && invocation.ApplicationId == host.ApplicationRevision.ApplicationId.Value
            && invocation.ApplicationRevision == host.ApplicationRevision.Revision
            && invocation.ApplicationFingerprint == host.ApplicationRevision.Fingerprint
            && invocation.BaseApplicationsJson == InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
                host.ApplicationRevision.BaseApplications.Select(value => value.ToString())))
            && invocation.GrantReference == host.GrantReference
            && invocation.Profile == InteractionExecutionProfile.ReadOnly;
    }

    internal static bool TryReadJudgment(SystemTaskCompletedValidationProof proof,
        SystemTaskValidationAuthority authority, out ApplicationCandidateReuseJudgmentOutputV2? judgment)
    {
        judgment = null;
        try
        {
            var result = InteractionCanonicalJson.CanonicalizeObject(proof.Snapshot.ResultJson!);
            var resultHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result)));
            if (proof.Snapshot.CompletionEvidenceReference != "validation.result." + resultHash
                || proof.Snapshot.Evidence.Count != 2
                || !proof.Snapshot.Evidence.Contains(authority.ReviewClosure!.EvidenceFingerprint, StringComparer.Ordinal)
                || !proof.Snapshot.Evidence.Contains(authority.Profile!.ManualContext.Reference, StringComparer.Ordinal))
                return false;
            using var resultDocument = JsonDocument.Parse(result);
            var root = resultDocument.RootElement;
            if (root.GetProperty("format").GetString() != "dantes-roleplay/application-candidate-reuse-review-result/v1"
                || root.GetProperty("closureEvidenceFingerprint").GetString() != authority.ReviewClosure.EvidenceFingerprint
                || root.GetProperty("inputFingerprint").GetString() != authority.ReviewInput!.InputFingerprint
                || !SameTask(root.GetProperty("task"), proof.Snapshot.Request.Handle)
                || !SameAttempt(root.GetProperty("attempt"), proof.Attempt)
                || InteractionCanonicalJson.Canonicalize(root.GetProperty("candidate").GetRawText())
                    != InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(authority.ReviewClosure.Candidate,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))))
                return false;
            judgment = ApplicationCandidateReuseJudgmentOutputV2.Parse(
                root.GetProperty("judgment").GetRawText(), authority.ReviewInput);

            using var admissionDocument = JsonDocument.Parse(proof.AdmissionPayloadJson);
            var admission = admissionDocument.RootElement;
            var matches = Equal(admission, "profileVersion", authority.Profile.ProfileVersion)
                && Equal(admission, "profileDefinition", authority.Profile.Profile)
                && Equal(admission, "manualContext", authority.Profile.ManualContext)
                && Equal(admission, "requiredContextReferences", authority.Profile.RequiredContextReferences)
                && Equal(admission, "validateAuthorityProvenance", authority.Profile.AuthorityProvenance)
                && Equal(admission, "readAuthorityProvenance", authority.Profile.ReadAuthorityProvenance)
                && Equal(admission, "aiBudget", authority.Profile.AiBudget)
                && admission.GetProperty("resultSchemaFingerprint").GetString() == authority.Profile.OutputSchemaFingerprint
                && proof.SchemaFingerprint == authority.Profile.OutputSchemaFingerprint
                && proof.ProfileFingerprint == authority.Profile.ProfileVersion.Fingerprint
                && proof.Budget == authority.Profile.AiBudget
                && authority.Profile.Worker.InputJson == proof.Snapshot.Request.InputJson
                && EnrollmentMatches(proof, authority.Profile);
            return matches;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or InteractionContractException or ArgumentException) { judgment = null; return false; }
    }

    private static bool JudgmentSupports((ImmutableArray<StandingGrantDefinitionReference> Predecessors, bool HasNew) structure,
        ApplicationCandidateReuseJudgmentOutputV2 judgment) => JudgmentSupports(structure.HasNew, judgment);

    internal static bool JudgmentSupports(bool hasNew, ApplicationCandidateReuseJudgmentOutputV2 judgment) => hasNew
        ? judgment.Judgment == ApplicationCandidateReuseJudgment.JustifiedNew
            && judgment.Assessments.All(value => value.Judgment == ApplicationCandidateReuseJudgment.JustifiedNew)
        : judgment.Judgment == ApplicationCandidateReuseJudgment.ExtendExisting
            && judgment.Assessments.All(value => value.Judgment == ApplicationCandidateReuseJudgment.ExtendExisting);

    private static bool Equal<T>(JsonElement parent, string name, T expected) =>
        parent.TryGetProperty(name, out var actual)
        && InteractionCanonicalJson.Canonicalize(actual.GetRawText())
            == InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(expected));
    private static bool SameTask(JsonElement value, SystemTaskDurableHandle expected) =>
        value.GetProperty("taskId").GetString() == expected.TaskId
        && value.GetProperty("commandId").GetString() == expected.CommandId;
    private static bool SameAttempt(JsonElement value, SystemTaskAttemptIdentity expected) =>
        value.GetProperty("stableCommandId").GetString() == expected.StableCommandId
        && value.GetProperty("attemptId").GetString() == expected.AttemptId
        && value.GetProperty("leaseToken").GetString() == expected.LeaseToken
        && value.GetProperty("fencingCounter").GetInt64() == expected.FencingCounter
        && value.GetProperty("leaseExpiresAtUtc").GetDateTime() == expected.LeaseExpiresAtUtc;

    private static bool EnrollmentMatches(SystemTaskCompletedValidationProof proof,
        SystemInnerWorkerResolvedProfile profile)
    {
        var candidate = ((SystemInnerWorkerSubject.ApplicationCandidateValidation)profile.Worker.Subject).Candidate;
        var inputFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.Worker.InputJson)));
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            purpose = "application-validation", candidate, profile.ProfileVersion,
            profileDefinition = profile.Profile, profile.OutputSchemaFingerprint, inputFingerprint,
            profile.ManualContext, profile.RequiredContextReferences,
            validateAuthorityProvenance = profile.AuthorityProvenance,
            readAuthorityProvenance = profile.ReadAuthorityProvenance,
            profile.Worker.InvocationHost.GrantReference,
            DeadlineUtc = proof.Snapshot.Request.Invocation.DeadlineUtc,
            profile.AiBudget
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return proof.EnrollmentFingerprint == InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/system-task-ai-enrollment/application-validation/v1", canonical);
    }
}

internal sealed class ApplicationCandidateReviewedPureUpdateEvidence
{
    private ApplicationCandidateReviewedPureUpdateEvidence(ApplicationCandidateRetainedMetadata retained,
        ActiveApplicationManifest basis, ApplicationCandidatePureRuntimeClosureEvidence closure,
        ImmutableArray<StandingGrantDefinitionReference> predecessors, SystemTaskCompletedValidationProof task,
        ApplicationCandidateReuseJudgmentOutputV2 judgment, ApplicationCandidateReuseInputV2 input,
        string causalCommandId)
    {
        Retained = retained; Basis = basis; Closure = closure; Predecessors = predecessors;
        Task = task; Judgment = judgment; Input = input; CausalCommandId = causalCommandId;
        Fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/reviewed-pure-mechanic-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Closure.Candidate, basis = Basis.ActivationFingerprint, Closure.EvidenceFingerprint,
                predecessors, task = Task.Snapshot.Request.Handle, Task.Snapshot.CompletionEvidenceReference,
                input.InputFingerprint, judgment.SelectionFingerprint, judgment.ManualResultFingerprint,
                judgment.Judgment, causalCommandId
            })));
    }
    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal ApplicationCandidatePureRuntimeClosureEvidence Closure { get; }
    internal ImmutableArray<StandingGrantDefinitionReference> Predecessors { get; }
    internal SystemTaskCompletedValidationProof Task { get; }
    internal ApplicationCandidateReuseJudgmentOutputV2 Judgment { get; }
    internal ApplicationCandidateReuseInputV2 Input { get; }
    internal string CausalCommandId { get; }
    internal string Fingerprint { get; }
    internal static ApplicationCandidateReviewedPureUpdateEvidence FromVerified(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidatePureRuntimeClosureEvidence closure,
        ImmutableArray<StandingGrantDefinitionReference> predecessors, SystemTaskCompletedValidationProof task,
        ApplicationCandidateReuseJudgmentOutputV2 judgment, ApplicationCandidateReuseInputV2 input,
        string causalCommandId) => new(retained, basis, closure, predecessors, task, judgment, input, causalCommandId);
}
