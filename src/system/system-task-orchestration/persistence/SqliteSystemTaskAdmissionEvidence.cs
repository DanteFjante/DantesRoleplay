using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    private static string ValidationAdmissionPayload(SystemInnerWorkerResolvedProfile profile,
        bool propagateCancellation, SystemTaskValidationCausation? causation)
    {
        var host = profile.Worker.InvocationHost;
        var candidate = ((SystemInnerWorkerSubject.ApplicationCandidateValidation)profile.Worker.Subject).Candidate;
        var dependencies = profile.Worker.DependencyHandles.OrderBy(value => value.TaskId, StringComparer.Ordinal)
            .Select(value => new { value.TaskId, value.CommandId }).ToArray();
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            purpose = "application-validation",
            principal = host.Principal.PrincipalId,
            authenticationMethod = host.Principal.AuthenticationMethod,
            application = host.ApplicationRevision.ApplicationId.ToString(),
            applicationRevision = host.ApplicationRevision.Revision,
            applicationFingerprint = host.ApplicationRevision.Fingerprint,
            baseApplications = InteractionCanonicalJson.Fingerprint(ValidationFingerprintDomain + "/bases",
                InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
                    host.ApplicationRevision.BaseApplications.Select(value => value.ToString())))),
            stateSpaceId = (string?)null,
            host.GrantReference,
            stateRevision = (string?)null,
            profile = InteractionExecutionProfileNames.Get(host.Profile),
            host.CommandId,
            host.ParentCommandId,
            maximumOperations = host.Budget.MaximumOperations,
            deadlineUtc = host.Budget.DeadlineUtc.ToString("O"),
            candidate,
            inputFingerprint = InteractionCanonicalJson.Fingerprint(ValidationFingerprintDomain + "/input", profile.Worker.InputJson),
            resultSchemaFingerprint = profile.OutputSchemaFingerprint,
            profileVersion = profile.ProfileVersion,
            profileDefinition = profile.Profile,
            manualContext = profile.ManualContext,
            requiredContextReferences = profile.RequiredContextReferences,
            validateAuthorityProvenance = profile.AuthorityProvenance,
            readAuthorityProvenance = profile.ReadAuthorityProvenance,
            aiBudget = profile.AiBudget,
            dependencies,
            propagateCancellation,
            causation = causation is null ? null : new { causation.OperationId, causation.CausalCommandId }
        }));
    }

    private static async Task<bool> ValidationAdmissionMatchesProfileAsync(SqliteConnection connection,
        SqliteTransaction transaction, SystemTaskStoredRequest task, SystemInnerWorkerResolvedProfile profile,
        CancellationToken cancellationToken)
    {
        if (task.Purpose != SystemTaskPurpose.ApplicationValidation) return true;
        var actual = await ScalarStringAsync(connection, transaction,
            "SELECT payload_fingerprint FROM system_task_lifecycle WHERE task_id=$task AND purpose='application-validation'",
            cancellationToken, ("$task", task.Handle.TaskId));
        return actual is not null && actual == InteractionCanonicalJson.Fingerprint(ValidationFingerprintDomain,
            ValidationAdmissionPayload(profile, task.PropagateCancellation, task.Causation));
    }

    private static SystemTaskValidationCausation? VerifyValidationAdmission(SqliteDataReader reader,
        SystemTaskStoredInvocation invocation, ApplicationCandidateReference candidate,
        IReadOnlyList<SystemTaskDurableHandle> dependencies)
    {
        try
        {
            var json = NullableString(reader, "admission_payload_json");
            if (json is null || InteractionCanonicalJson.CanonicalizeObject(json) != json
                || InteractionCanonicalJson.Fingerprint(ValidationFingerprintDomain, json)
                    != reader.GetString(reader.GetOrdinal("payload_fingerprint")))
                throw new InvalidDataException("The retained validation admission payload does not match its immutable commitment.");
            using var document = JsonDocument.Parse(json);
            var payload = document.RootElement;
            bool Equal<T>(string name, T value) => payload.TryGetProperty(name, out var property)
                && InteractionCanonicalJson.Canonicalize(property.GetRawText())
                    == InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(value));

            var valid = Equal("purpose", "application-validation")
                && Equal("principal", invocation.PrincipalReference)
                && Equal("authenticationMethod", invocation.AuthenticationMethod)
                && Equal("application", invocation.ApplicationId)
                && Equal("applicationRevision", invocation.ApplicationRevision)
                && Equal("applicationFingerprint", invocation.ApplicationFingerprint)
                && Equal("baseApplications", InteractionCanonicalJson.Fingerprint(ValidationFingerprintDomain + "/bases",
                    invocation.BaseApplicationsJson))
                && Equal("stateSpaceId", (string?)null)
                && Equal("GrantReference", invocation.GrantReference)
                && Equal("stateRevision", (string?)null)
                && Equal("profile", InteractionExecutionProfileNames.Get(invocation.Profile))
                && Equal("CommandId", invocation.CommandId)
                && Equal("ParentCommandId", invocation.ParentCommandId)
                && Equal("deadlineUtc", invocation.DeadlineUtc.ToString("O"))
                && Equal("candidate", candidate)
                && Equal("inputFingerprint", InteractionCanonicalJson.Fingerprint(
                    ValidationFingerprintDomain + "/input", reader.GetString(reader.GetOrdinal("input_json"))))
                && Equal("dependencies", dependencies.OrderBy(value => value.TaskId, StringComparer.Ordinal)
                    .Select(value => new { value.TaskId, value.CommandId }).ToArray())
                && Equal("propagateCancellation", reader.GetInt64(reader.GetOrdinal("propagate_cancellation")) != 0);
            if (!valid || invocation.StateSpaceId is not null || invocation.StateRevision is not null
                || invocation.Profile != InteractionExecutionProfile.ReadOnly
                || !payload.TryGetProperty("maximumOperations", out var maximum)
                || !maximum.TryGetInt32(out var operations)
                || operations is < 1 or > SystemTaskLifecycleLimits.MaximumRootOperations
                || invocation.AdmittedOperations > operations)
                throw new InvalidDataException("The retained validation identity was not committed by its admission payload.");

            var operationId = NullableString(reader, "causation_operation_id");
            if (!payload.TryGetProperty("causation", out var cause))
                throw new InvalidDataException("The retained validation causation shape is missing.");
            if (operationId is null)
            {
                if (cause.ValueKind != JsonValueKind.Null) throw new InvalidDataException(
                    "The retained validation causation does not match its operation reference.");
                return null;
            }
            if (cause.ValueKind != JsonValueKind.Object
                || !cause.TryGetProperty("OperationId", out var operationProperty)
                || operationProperty.GetString() != operationId
                || !cause.TryGetProperty("CausalCommandId", out var commandProperty)
                || commandProperty.GetString() is not { } causalCommandId
                || !ValidOperationId(operationId)
                || !ValidCausalCommandId(causalCommandId))
                throw new InvalidDataException("The retained validation causation identity is invalid.");
            return new(operationId, causalCommandId);
        }
        catch (Exception error) when (error is InteractionContractException or JsonException
            or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("The retained validation admission proof is invalid.", error);
        }
    }

    private static bool ValidOperationId(string? value) => value is { Length: 32 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static bool ValidCausalCommandId(string? value) => value is { Length: > 0 }
        && value.Length <= InteractionContractLimits.IdempotencyKey
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    private static void VerifyActivationAdmission(SqliteDataReader reader, SystemTaskStoredInvocation invocation,
        SystemTaskSelectedDefinition definition, IReadOnlyList<SystemTaskDurableHandle> dependencies,
        StandingGrantActivationOrigin origin)
    {
        try
        {
            var json = NullableString(reader, "admission_payload_json");
            if (json is null || InteractionCanonicalJson.CanonicalizeObject(json) != json
                || InteractionCanonicalJson.Fingerprint(FingerprintDomain, json) != reader.GetString(reader.GetOrdinal("payload_fingerprint")))
                throw new InvalidDataException("The retained admission payload does not match the task's immutable commitment.");
            using var document = JsonDocument.Parse(json);
            var payload = document.RootElement;
            bool Equal<T>(string name, T value) => payload.TryGetProperty(name, out var property)
                && InteractionCanonicalJson.Canonicalize(property.GetRawText())
                    == InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(value));

            // Compare immutable retained identity only. Requested MaximumOperations and the
            // original checkpoint cannot be reconstructed from current remaining/state columns.
            // Their original values remain committed in these exact admission bytes instead.
            var valid = Equal("principal", invocation.PrincipalReference)
                && Equal("authenticationMethod", invocation.AuthenticationMethod)
                && Equal("application", invocation.ApplicationId)
                && Equal("applicationRevision", invocation.ApplicationRevision)
                && Equal("applicationFingerprint", invocation.ApplicationFingerprint)
                && Equal("baseApplications", InteractionCanonicalJson.Fingerprint(FingerprintDomain + "/bases", invocation.BaseApplicationsJson))
                && Equal("StateSpaceId", invocation.StateSpaceId)
                && Equal("GrantReference", invocation.GrantReference)
                && Equal("StateRevision", invocation.StateRevision)
                && Equal("profile", InteractionExecutionProfileNames.Get(invocation.Profile))
                && Equal("CommandId", invocation.CommandId)
                && Equal("ParentCommandId", invocation.ParentCommandId)
                && Equal("deadlineUtc", invocation.DeadlineUtc.ToString("O"))
                && Equal("definitionId", definition.ExactDefinitionId)
                && Equal("definitionVersion", definition.Version)
                && Equal("definitionFingerprint", definition.Fingerprint)
                && Equal("inputFingerprint", InteractionCanonicalJson.Fingerprint(FingerprintDomain + "/input", reader.GetString(reader.GetOrdinal("input_json"))))
                && Equal("propagateCancellation", reader.GetInt64(reader.GetOrdinal("propagate_cancellation")) != 0)
                && Equal("dependencies", dependencies.OrderBy(value => value.TaskId, StringComparer.Ordinal)
                    .Select(value => new { value.TaskId, value.CommandId }).ToArray())
                && Equal("activationOrigin", new
                {
                    activationRevision = origin.ActivationRevision,
                    activationFingerprint = origin.ActivationFingerprint,
                    applicationRevision = origin.ApplicationRevision,
                    applicationFingerprint = origin.ApplicationFingerprint
                });
            if (!valid || !payload.TryGetProperty("maximumOperations", out var maximum)
                || !maximum.TryGetInt32(out var operations) || operations is < 1 or > SystemTaskLifecycleLimits.MaximumRootOperations
                || invocation.AdmittedOperations > operations)
                throw new InvalidDataException("The retained task identity or activation origin was not committed by this admission payload.");
        }
        catch (Exception error) when (error is InteractionContractException or JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The retained admission proof is invalid.", error);
        }
    }
}
