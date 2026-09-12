using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Canonical durable envelope for workflow runtime reports; separate from pure and atomic reports.</summary>
internal static class ApplicationCandidateWorkflowOperationProof
{
    private static readonly JsonSerializerOptions ReportJson = CreateReportJson();

    internal static string EvidenceReference(string operationId, string reportFingerprint) =>
        operationId + "#workflow-runtime-report." + reportFingerprint;

    internal static string Guard(InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateValidationRecord row, IReadOnlyList<StandingGrantDefinitionReference> definitions,
        string commandFingerprint, ApplicationCandidateRuntimeReport report) =>
        Guard(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, candidate.ApplicationId.Value,
            host.CommandId, candidate, row, definitions, commandFingerprint, report);

    internal static bool TryRead(Operation operation, ApplicationCandidateValidationRecord row,
        ApplicationCandidateReference candidate, IReadOnlyList<StandingGrantDefinitionReference> definitions,
        out ApplicationCandidateValidationRequest? request, out ApplicationCandidateRuntimeReport? report)
    {
        request = null;
        report = null;
        try
        {
            if (operation.Tool != "application-candidate-validation" || !operation.Success
                || operation.Subject != candidate.ApplicationId.Value || operation.Id != row.OperationId
                || row.ApplicationId != candidate.ApplicationId.Value || row.CandidateId != candidate.CandidateId
                || row.Revision != candidate.Revision || row.CandidateFingerprint != candidate.ContentFingerprint
                || !TryCommand(operation.ProjectionJson, candidate, out var command) || command is null
                || operation.Id != ApplicationCandidateOperationProof.OperationId(command.Principal,
                    command.ApplicationId, command.CommandId, "validation")
                || InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-validation/v1",
                    operation.ProjectionJson) != row.CanonicalCommandFingerprint
                || command.Samples.Any(sample => !definitions.Contains(sample.Definition))) return false;
            using var guard = JsonDocument.Parse(operation.GuardEvidenceJson);
            var root = guard.RootElement;
            if (!Exact(root, "authorization", "commandFingerprint", "candidate", "grantReference",
                    "selectedDefinitions", "validationFingerprint", "workflowRuntimeReportFingerprint",
                    "workflowRuntimeReport")
                || root.GetProperty("workflowRuntimeReport").Deserialize<ApplicationCandidateRuntimeReport>(ReportJson) is not { } parsed
                || parsed.Candidate != candidate
                || (parsed.SelectionEvidenceFingerprint is null
                    ? parsed.RuntimePolicyVersion is not null || parsed.RuntimePolicyFingerprint is not null
                    : parsed.RuntimePolicyVersion != ApplicationCandidateWorkflowRuntimeValidator.PolicyVersion
                        || parsed.RuntimePolicyFingerprint != ApplicationCandidateWorkflowRuntimeValidator.PolicyFingerprint)
                || parsed.Samples is null || parsed.Diagnostics is null
                || parsed.Samples.Count > command.Samples.Count
                || parsed.Diagnostics.Count > ApplicationAuthoringLimits.Diagnostics
                || parsed.Dependencies is { Count: > ApplicationAuthoringLimits.Dependencies }
                || parsed.Status == ApplicationCandidateRuntimeStatus.Completed
                    && (parsed.SelectionEvidenceFingerprint is null || parsed.Diagnostics.Count != 0
                        || parsed.Samples.Count != command.Samples.Count)
                || parsed.Status == ApplicationCandidateRuntimeStatus.Invalid && row.Outcome != "invalid"
                || parsed.Status == ApplicationCandidateRuntimeStatus.Unavailable && row.Outcome != "unavailable"
                || parsed.Status == ApplicationCandidateRuntimeStatus.Completed && row.Outcome is not ("valid" or "unavailable")
                || Encoding.UTF8.GetByteCount(InteractionCanonicalJson.CanonicalizeObject(
                    JsonSerializer.Serialize(parsed))) > InteractionContractLimits.JsonBytes) return false;
            var fingerprint = ApplicationCandidateWorkflowRuntimeValidator.ReportFingerprint(parsed);
            if (root.GetProperty("workflowRuntimeReportFingerprint").GetString() != fingerprint
                || row.PreparedEvidenceReference != EvidenceReference(operation.Id, fingerprint)
                || row.PreparationVersion != ApplicationCandidateWorkflowRuntimeValidator.PolicyVersion
                    + "@" + ApplicationCandidateWorkflowRuntimeValidator.PolicyFingerprint
                || operation.GuardEvidenceJson != Guard(command.Principal, command.AuthenticationMethod,
                    command.ApplicationId, command.CommandId, candidate, row, definitions,
                    row.CanonicalCommandFingerprint, parsed)) return false;
            request = new(candidate, command.Samples);
            report = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static string Guard(string principal, string authenticationMethod, string applicationId,
        string commandId, ApplicationCandidateReference candidate, ApplicationCandidateValidationRecord row,
        IReadOnlyList<StandingGrantDefinitionReference> definitions, string commandFingerprint,
        ApplicationCandidateRuntimeReport report)
    {
        var authorization = new AuthorizationAuditEvidence(principal, authenticationMethod,
            "standing-grant", applicationId, commandId, true, "STANDING_GRANT_ALLOWED");
        var selectedDefinitions = definitions.OrderBy(value => value.DefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.Kind, StringComparer.Ordinal).ThenBy(value => value.Revision)
            .ThenBy(value => value.ContentFingerprint, StringComparer.Ordinal)
            .Select(value => new { value.DefinitionId, value.Kind, value.Revision, value.ContentFingerprint }).ToArray();
        var workflowRuntimeReportFingerprint = ApplicationCandidateWorkflowRuntimeValidator.ReportFingerprint(report);
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            authorization, commandFingerprint, candidate, grantReference = row.GrantReference,
            selectedDefinitions, validationFingerprint = ApplicationCandidateOperationProof.ValidationFingerprint(row),
            workflowRuntimeReportFingerprint, workflowRuntimeReport = report
        }));
    }

    private static bool TryCommand(string json, ApplicationCandidateReference candidate,
        out ValidationCommand? command)
    {
        command = null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!Exact(root, "principal", "authenticationMethod", "applicationId", "CommandId", "candidate", "samples")
            || InteractionCanonicalJson.CanonicalizeObject(root.GetProperty("candidate").GetRawText())
                != InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(candidate))
            || root.GetProperty("samples").ValueKind != JsonValueKind.Array) return false;
        var principal = root.GetProperty("principal").GetString();
        var method = root.GetProperty("authenticationMethod").GetString();
        var application = root.GetProperty("applicationId").GetString();
        var commandId = root.GetProperty("CommandId").GetString();
        if (!TrustedPrincipalContext.IsValidPrincipalId(principal) || string.IsNullOrWhiteSpace(method)
            || method.Length > 64 || application != candidate.ApplicationId.Value
            || commandId is not { Length: > 0 and <= InteractionContractLimits.IdempotencyKey }) return false;
        var samples = root.GetProperty("samples").EnumerateArray()
            .Select(value => value.Deserialize<ApplicationCandidateValidationSample>()!).ToArray();
        if (samples.Any(value => value is null)) return false;
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        { principal, authenticationMethod = method, applicationId = application, CommandId = commandId, candidate, samples }));
        if (canonical != json) return false;
        command = new(principal!, method!, application!, commandId!, samples);
        return true;
    }

    private static bool Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        return actual.Length == names.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length
            && actual.All(name => names.Contains(name, StringComparer.Ordinal));
    }

    private static JsonSerializerOptions CreateReportJson()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new RetainedApplicationIdentifierConverter());
        return options;
    }

    private sealed class RetainedApplicationIdentifierConverter : System.Text.Json.Serialization.JsonConverter<ApplicationIdentifier>
    {
        public override ApplicationIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!Exact(root, "Value", "IsSystem")
                || root.GetProperty("Value").GetString() is not { } value
                || root.GetProperty("IsSystem").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || root.GetProperty("IsSystem").GetBoolean() != (value == ApplicationIdentifier.System.Value))
                throw new JsonException("The retained application identifier is invalid.");
            return value == ApplicationIdentifier.System.Value
                ? ApplicationIdentifier.System : ApplicationIdentifier.Parse(value);
        }

        public override void Write(Utf8JsonWriter writer, ApplicationIdentifier value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("Value", value.Value);
            writer.WriteBoolean("IsSystem", value.IsSystem);
            writer.WriteEndObject();
        }
    }

    private sealed record ValidationCommand(string Principal, string AuthenticationMethod,
        string ApplicationId, string CommandId, IReadOnlyList<ApplicationCandidateValidationSample> Samples);
}
