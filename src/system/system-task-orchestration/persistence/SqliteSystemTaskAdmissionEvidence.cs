using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
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
