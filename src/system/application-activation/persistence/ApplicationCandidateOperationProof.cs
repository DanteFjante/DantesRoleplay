using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;

namespace DantesRoleplay.ApplicationActivation;

internal sealed record ApplicationCandidateOriginalCommand(string Principal, string AuthenticationMethod,
    string ApplicationId, string CommandId, string CandidateId, ApplicationCandidateWriteRequest Request,
    string CanonicalCommand);

internal static class ApplicationCandidateOperationProof
{
    private const string WriteTool = "application-candidate";
    private const string ValidationTool = "application-candidate-validation";

    internal static string CanonicalWrite(InteractionInvocationHost host, ApplicationCandidateWriteRequest request, string id) =>
        CanonicalWrite(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, host.ApplicationRevision.ApplicationId.Value,
            host.CommandId, request, id);

    internal static string CanonicalValidation(InteractionInvocationHost host, ApplicationCandidateReference candidate,
        IReadOnlyList<ApplicationCandidateValidationSample> samples) =>
        CanonicalValidation(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, candidate.ApplicationId.Value,
            host.CommandId, candidate, samples);

    internal static string OperationId(string principal, string applicationId, string commandId, string suffix = "candidate") =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("dantes-roleplay/application-candidate/" + suffix
            + "/v1\n" + principal + "\n" + applicationId + "\n" + commandId)))[..32].ToLowerInvariant();

    internal static string WriteGuard(InteractionInvocationHost host, ApplicationCandidateReference candidate,
        string grantReference, IReadOnlyList<StandingGrantDefinitionReference> definitions, string commandFingerprint) =>
        Guard(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, host.ApplicationRevision.ApplicationId.Value,
            host.CommandId, candidate, grantReference, definitions, commandFingerprint, null);

    internal static string ValidationGuard(InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateValidationRecord row, IReadOnlyList<StandingGrantDefinitionReference> definitions,
        string commandFingerprint, ApplicationCandidateRuntimeReport? runtimeReport = null) =>
        Guard(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, candidate.ApplicationId.Value,
            host.CommandId, candidate, row.GrantReference, definitions, commandFingerprint,
            ValidationFingerprint(row), runtimeReport);

    internal static bool WriteMatches(Operation operation, ApplicationCandidateRetainedMetadata metadata,
        IReadOnlyList<StandingGrantDefinitionReference> actualDefinitions, out ApplicationCandidateOriginalCommand? original)
    {
        original = null;
        if (operation.Tool != WriteTool || !operation.Success || operation.Subject != metadata.ApplicationRevision.ApplicationId.Value)
            return false;
        if (!TryWriteCommand(operation.ProjectionJson, out var command) || command is null) return false;
        var row = metadata.RevisionRow;
        if (!ValidOriginalIdentity(command.Principal, command.AuthenticationMethod, command.CommandId)
            || operation.Id != row.SourceOperationId || operation.Id != OperationId(command.Principal, command.ApplicationId, command.CommandId, "operation")
            || command.ApplicationId != row.ApplicationId || command.CandidateId != row.CandidateId
            || command.Request.CandidateId != row.CandidateId || command.Request.ExpectedCandidateRevision + 1 != row.Revision
            || command.Request.Origin != row.Origin || command.Request.ExpectedActiveFingerprint != row.ExpectedActiveFingerprint
            || command.Request.NewImplementationReason.Trim() != row.NewImplementationReason
            || command.Request.SynchronizationEvidenceReference != row.SynchronizationEvidenceReference
            || command.CanonicalCommand != operation.ProjectionJson
            || InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-command/v1", command.CanonicalCommand) != row.CanonicalCommandFingerprint
            || !DocumentsMatch(command.Request, metadata.Documents)) return false;
        if (command.Request.ExpectedCandidateRevision == 0
            && command.CandidateId != OperationId(command.Principal, command.ApplicationId, command.CommandId)) return false;
        var candidate = new ApplicationCandidateReference(metadata.ApplicationRevision.ApplicationId, row.CandidateId, row.Revision, row.ContentFingerprint);
        if (operation.GuardEvidenceJson != Guard(command.Principal, command.AuthenticationMethod, command.ApplicationId,
                command.CommandId, candidate, row.AuthorGrantReference, actualDefinitions, row.CanonicalCommandFingerprint, null)) return false;
        original = command;
        return true;
    }

    internal static bool ValidationMatches(Operation operation, ApplicationCandidateValidationRecord row,
        ApplicationCandidateReference candidate, IReadOnlyList<StandingGrantDefinitionReference> actualDefinitions)
    {
        if (operation.Tool != ValidationTool || !operation.Success || operation.Subject != candidate.ApplicationId.Value
            || row.ApplicationId != candidate.ApplicationId.Value || row.CandidateId != candidate.CandidateId
            || row.Revision != candidate.Revision || row.CandidateFingerprint != candidate.ContentFingerprint || operation.Id != row.OperationId)
            return false;
        if (!TryValidationCommand(operation.ProjectionJson, candidate, out var command) || command is null
            || !ValidOriginalIdentity(command.principal, command.authenticationMethod, command.CommandId)
            || command.applicationId is null)
            return false;
        var principal = command.principal!;
        var authenticationMethod = command.authenticationMethod!;
        var applicationId = command.applicationId;
        var commandId = command.CommandId!;
        if (operation.Id != OperationId(principal, applicationId, commandId, "validation")
            || applicationId != candidate.ApplicationId.Value || command.CanonicalCommand != operation.ProjectionJson
            || InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-validation/v1", command.CanonicalCommand) != row.CanonicalCommandFingerprint)
            return false;
        if (row.PreparationVersion == ApplicationCandidateIntentMatchUpdateValidation.PreparationVersion)
            return row.Outcome == "valid" && row.DependenciesComplete
                && row.ManualPacketResultFingerprint is { Length: 64 }
                && row.PreparedEvidenceReference is { Length: 97 } reference && reference[32] == ':'
                && row.DependencyEvidenceReference == reference && row.ReuseEvidenceReference == reference
                && operation.GuardEvidenceJson == Guard(principal, authenticationMethod, applicationId,
                    commandId, candidate, row.GrantReference, actualDefinitions,
                    row.CanonicalCommandFingerprint, ValidationFingerprint(row), null);
        if (row.PreparationVersion is { } statefulVersion
            && statefulVersion.StartsWith(ApplicationCandidateStatefulRuntimeValidator.PolicyVersion + "@", StringComparison.Ordinal))
            return TryReadStatefulRuntimeReport(operation, row, candidate, actualDefinitions, out _);
        if (row.PreparedEvidenceReference is not null || row.PreparationVersion is not null)
            return TryReadRuntimeReport(operation, row, candidate, actualDefinitions, out _);
        return operation.GuardEvidenceJson == Guard(principal, authenticationMethod, applicationId,
            commandId, candidate, row.GrantReference, actualDefinitions, row.CanonicalCommandFingerprint,
            ValidationFingerprint(row), null);
    }

    internal static string ValidationFingerprint(ApplicationCandidateValidationRecord row) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-validation-outcome/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(row)));

    internal static string RuntimeReportFingerprint(ApplicationCandidateRuntimeReport report) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-runtime-report/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(report)));

    internal static string RuntimeEvidenceReference(string operationId, string reportFingerprint) =>
        operationId + "#runtime-report." + reportFingerprint;

    internal static string StatefulRuntimeReportFingerprint(ApplicationCandidateStatefulRuntimeReport report) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-stateful-runtime-report/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(report)));

    internal static string StatefulRuntimeEvidenceReference(string operationId, string reportFingerprint) =>
        operationId + "#stateful-runtime-report." + reportFingerprint;

    internal static string StatefulValidationGuard(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, ApplicationCandidateValidationRecord row,
        IReadOnlyList<StandingGrantDefinitionReference> definitions, string commandFingerprint,
        ApplicationCandidateStatefulRuntimeReport report) => StatefulValidationGuard(
            host.Principal.PrincipalId, host.Principal.AuthenticationMethod, candidate.ApplicationId.Value,
            host.CommandId, candidate, row, definitions, commandFingerprint, report);

    private static string StatefulValidationGuard(string principal, string authenticationMethod,
        string applicationId, string commandId, ApplicationCandidateReference candidate,
        ApplicationCandidateValidationRecord row, IReadOnlyList<StandingGrantDefinitionReference> definitions,
        string commandFingerprint, ApplicationCandidateStatefulRuntimeReport report)
    {
        var authorization = new AuthorizationAuditEvidence(principal, authenticationMethod,
            "standing-grant", applicationId, commandId, true, "STANDING_GRANT_ALLOWED");
        var selectedDefinitions = definitions.OrderBy(value => value.DefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.Kind, StringComparer.Ordinal).ThenBy(value => value.Revision)
            .ThenBy(value => value.ContentFingerprint, StringComparer.Ordinal)
            .Select(value => new { value.DefinitionId, value.Kind, value.Revision, value.ContentFingerprint }).ToArray();
        var reportFingerprint = StatefulRuntimeReportFingerprint(report);
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            authorization, commandFingerprint, candidate, grantReference = row.GrantReference,
            selectedDefinitions, validationFingerprint = ValidationFingerprint(row),
            statefulRuntimeReportFingerprint = reportFingerprint, statefulRuntimeReport = report
        }));
    }

    internal static bool TryValidateStatefulRuntimeReport(ApplicationCandidateStatefulRuntimeReport? report,
        ApplicationCandidateValidationRequest request, out string fingerprint)
    {
        fingerprint = string.Empty;
        try
        {
            if (report is null || report.Candidate != request.Candidate || !Hash(report.UpdateFingerprint)
                || report.PolicyVersion != ApplicationCandidateStatefulRuntimeValidator.PolicyVersion
                || report.PolicyFingerprint != ApplicationCandidateStatefulRuntimeValidator.PolicyFingerprint
                || report.Dependencies is null || report.Dependencies.Count == 0
                || report.Dependencies.Any(value => value.Revision < 1 || !Hash(value.ContentFingerprint))
                || string.IsNullOrWhiteSpace(report.StateGrantReference) || !Hash(report.StateGrantFingerprint)
                || report.EffectKinds is null || report.EffectKinds.Any(value =>
                    !EcsEffects.ApplicationEcsEffectType.All.Contains(value, StringComparer.Ordinal))
                || report.Samples is null || report.Samples.Count != request.Samples.Count) return false;
            for (var index = 0; index < report.Samples.Count; index++)
            {
                var actual = report.Samples[index];
                var expected = request.Samples[index];
                if (expected.StateSpaceId is null || expected.StateRevision is null
                    || expected.RoleEntityIds is null || expected.ExpectedEffectsJson is null
                    || actual.Definition != expected.Definition || actual.SampleIndex != index
                    || actual.StateSpaceId != expected.StateSpaceId || actual.StateRevision != expected.StateRevision
                    || !actual.RoleEntityIds.SequenceEqual(expected.RoleEntityIds)
                    || actual.InputFingerprint != ApplicationCandidateStatefulRuntimeValidator.DataFingerprint(expected.InputJson)
                    || actual.ExpectedDataFingerprint != ApplicationCandidateStatefulRuntimeValidator.DataFingerprint(expected.ExpectedDataJson)
                    || actual.ActualDataFingerprint != actual.ExpectedDataFingerprint
                    || actual.ExpectedEffectsFingerprint != ApplicationCandidateStatefulRuntimeValidator.DataFingerprint(expected.ExpectedEffectsJson)
                    || actual.ActualEffectsFingerprint != actual.ExpectedEffectsFingerprint
                    || !Hash(actual.ProjectionFingerprint) || !Hash(actual.BatchFingerprint)
                    || !TryValidateStatefulBatch(actual)
                    || actual.DryRunOperationId is not { Length: 32 }) return false;
            }
            var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(report));
            if (Encoding.UTF8.GetByteCount(canonical) > InteractionContractLimits.JsonBytes) return false;
            fingerprint = StatefulRuntimeReportFingerprint(report);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    internal static bool TryReadStatefulRuntimeReport(Operation operation, ApplicationCandidateValidationRecord row,
        ApplicationCandidateReference candidate, IReadOnlyList<StandingGrantDefinitionReference> actualDefinitions,
        out ApplicationCandidateStatefulRuntimeReport? report)
    {
        report = null;
        try
        {
            if (operation.Tool != ValidationTool || !operation.Success || operation.Subject != candidate.ApplicationId.Value
                || operation.Id != row.OperationId || row.ApplicationId != candidate.ApplicationId.Value
                || row.CandidateId != candidate.CandidateId || row.Revision != candidate.Revision
                || row.CandidateFingerprint != candidate.ContentFingerprint || row.Outcome != "valid"
                || !TryValidationCommand(operation.ProjectionJson, candidate, out var command) || command is null
                || command.applicationId != candidate.ApplicationId.Value
                || command.Samples.Any(sample => !actualDefinitions.Contains(sample.Definition))) return false;
            using var guard = JsonDocument.Parse(operation.GuardEvidenceJson);
            var root = guard.RootElement;
            if (!root.TryGetProperty("statefulRuntimeReport", out var reportElement)
                || !root.TryGetProperty("statefulRuntimeReportFingerprint", out var fingerprintElement)
                || fingerprintElement.ValueKind != JsonValueKind.String) return false;
            if (!TryParseStatefulRuntimeReport(reportElement, candidate, out var parsed) || parsed is null) return false;
            var request = new ApplicationCandidateValidationRequest(candidate, command.Samples);
            if (!TryValidateStatefulRuntimeReport(parsed, request, out var fingerprint)
                || fingerprintElement.GetString() != fingerprint
                || row.PreparedEvidenceReference != StatefulRuntimeEvidenceReference(operation.Id, fingerprint)
                || row.PreparationVersion != parsed.PolicyVersion + "@" + parsed.PolicyFingerprint
                || operation.GuardEvidenceJson != StatefulValidationGuard(command.principal!,
                    command.authenticationMethod!, command.applicationId!, command.CommandId!, candidate,
                    row, actualDefinitions, row.CanonicalCommandFingerprint, parsed)) return false;
            report = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static bool TryParseStatefulRuntimeReport(JsonElement element, ApplicationCandidateReference candidate,
        out ApplicationCandidateStatefulRuntimeReport? report)
    {
        report = null;
        try
        {
            if (element.ValueKind != JsonValueKind.Object
                || InteractionCanonicalJson.CanonicalizeObject(element.GetProperty("Candidate").GetRawText())
                    != InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(candidate))) return false;
            var predecessor = Definition(element.GetProperty("RootPredecessor"));
            var dependencies = element.GetProperty("Dependencies").EnumerateArray().Select(Definition).ToArray();
            var effectKinds = element.GetProperty("EffectKinds").EnumerateArray()
                .Select(value => value.GetString() ?? throw new JsonException()).ToArray();
            var samples = new List<ApplicationCandidateStatefulSampleResult>();
            foreach (var value in element.GetProperty("Samples").EnumerateArray())
            {
                var roles = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in value.GetProperty("RoleEntityIds").EnumerateObject())
                    roles.Add(property.Name, property.Value.GetString() ?? throw new JsonException());
                samples.Add(new(Definition(value.GetProperty("Definition")),
                    value.GetProperty("SampleIndex").GetInt32(), value.GetProperty("StateSpaceId").GetString()!,
                    value.GetProperty("StateRevision").GetString()!, roles,
                    value.GetProperty("InputFingerprint").GetString()!,
                    value.GetProperty("ExpectedDataFingerprint").GetString()!,
                    value.GetProperty("ActualDataFingerprint").GetString()!,
                    value.GetProperty("ExpectedEffectsFingerprint").GetString()!,
                    value.GetProperty("ActualEffectsFingerprint").GetString()!,
                    value.GetProperty("ProjectionFingerprint").GetString()!,
                    value.GetProperty("BatchJson").GetString()!,
                    value.GetProperty("BatchFingerprint").GetString()!,
                    value.GetProperty("DryRunOperationId").GetString()!));
            }
            report = new(candidate, element.GetProperty("UpdateFingerprint").GetString()!,
                element.GetProperty("PolicyVersion").GetString()!, element.GetProperty("PolicyFingerprint").GetString()!,
                predecessor, dependencies, element.GetProperty("StateGrantReference").GetString()!,
                element.GetProperty("StateGrantFingerprint").GetString()!, effectKinds, samples.AsReadOnly());
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static StandingGrantDefinitionReference Definition(JsonElement element) => new(
        element.GetProperty("DefinitionId").GetString()!, element.GetProperty("Kind").GetString()!,
        element.GetProperty("Revision").GetInt32(), element.GetProperty("ContentFingerprint").GetString()!);

    private static bool TryValidateStatefulBatch(ApplicationCandidateStatefulSampleResult sample)
    {
        try
        {
            if (InteractionCanonicalJson.CanonicalizeObject(sample.BatchJson) != sample.BatchJson
                || ApplicationCandidateStatefulRuntimeValidator.DataFingerprint(sample.BatchJson) != sample.BatchFingerprint)
                return false;
            using var document = JsonDocument.Parse(sample.BatchJson);
            var root = document.RootElement;
            return root.GetProperty("StateSpaceId").GetString() == sample.StateSpaceId
                && root.GetProperty("MechanicId").GetString() == sample.Definition.DefinitionId
                && root.GetProperty("MechanicVersion").GetInt32() == sample.Definition.Revision
                && root.GetProperty("ExecutionIdentity").ValueKind == JsonValueKind.Null
                && ApplicationCandidateStatefulRuntimeValidator.DataFingerprint(
                    root.GetProperty("ProjectionJson").GetString()!) == sample.ProjectionFingerprint
                && ApplicationCandidateStatefulRuntimeValidator.DataFingerprint(
                    root.GetProperty("Effects").GetRawText()) == sample.ExpectedEffectsFingerprint;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    internal static bool TryValidateRuntimeReport(ApplicationCandidateRuntimeReport? report,
        ApplicationCandidateValidationRequest request, out string canonical, out string fingerprint)
    {
        canonical = string.Empty;
        fingerprint = string.Empty;
        try
        {
            if (report is null || report.Candidate != request.Candidate
                || !Enum.IsDefined(report.Status) || report.Samples is null || report.Diagnostics is null
                || report.Samples.Count > request.Samples.Count
                || report.Diagnostics.Count > ApplicationAuthoringLimits.Diagnostics)
                return false;
            if (report.SelectionEvidenceFingerprint is null)
            {
                if (report.RuntimePolicyVersion is not null || report.RuntimePolicyFingerprint is not null
                    || report.Samples.Count != 0) return false;
            }
            else if (!Hash(report.SelectionEvidenceFingerprint)
                || report.RuntimePolicyVersion != ApplicationCandidateRuntimeValidator.RuntimePolicyVersion
                || report.RuntimePolicyFingerprint != ApplicationCandidateRuntimeValidator.RuntimePolicyFingerprint)
                return false;
            if (report.Status == ApplicationCandidateRuntimeStatus.Completed
                && (report.Samples.Count != request.Samples.Count || report.Diagnostics.Count != 0)) return false;
            if (report.Status != ApplicationCandidateRuntimeStatus.Completed && report.Diagnostics.Count == 0) return false;
            if (report.Diagnostics.Any(value => value is null || string.IsNullOrWhiteSpace(value.Code)
                    || string.IsNullOrWhiteSpace(value.Target) || string.IsNullOrWhiteSpace(value.Message)
                    || value.Code.Length > ApplicationAuthoringLimits.DiagnosticCharacters
                    || value.Target.Length > ApplicationAuthoringLimits.DiagnosticCharacters
                    || value.Message.Length > ApplicationAuthoringLimits.DiagnosticCharacters)) return false;
            for (var index = 0; index < report.Samples.Count; index++)
            {
                var actual = report.Samples[index];
                var expected = request.Samples[index];
                if (actual is null || actual.SampleIndex != index || actual.Definition != expected.Definition
                    || !Enum.IsDefined(actual.Outcome)
                    || actual.InputFingerprint != RuntimeDataFingerprint(expected.InputJson)
                    || actual.ExpectedDataFingerprint != RuntimeDataFingerprint(expected.ExpectedDataJson)
                    || actual.ActualDataFingerprint is { } actualFingerprint && !Hash(actualFingerprint)
                    || !actual.Attempted && actual.ActualDataFingerprint is not null
                    || actual.Outcome == ApplicationCandidateRuntimeStatus.Completed
                        && (!actual.Attempted || actual.ActualDataFingerprint != actual.ExpectedDataFingerprint)
                    || index < report.Samples.Count - 1
                        && actual.Outcome != ApplicationCandidateRuntimeStatus.Completed)
                    return false;
            }
            if (report.Status == ApplicationCandidateRuntimeStatus.Completed
                && report.Samples.Any(value => value.Outcome != ApplicationCandidateRuntimeStatus.Completed)) return false;
            canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(report));
            if (Encoding.UTF8.GetByteCount(canonical) > InteractionContractLimits.JsonBytes) return false;
            fingerprint = InteractionCanonicalJson.Fingerprint(
                "dantes-roleplay/application-candidate-runtime-report/v1", canonical);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    internal static bool TryReadRuntimeReport(Operation operation, ApplicationCandidateValidationRecord row,
        ApplicationCandidateReference candidate, IReadOnlyList<StandingGrantDefinitionReference> actualDefinitions,
        out ApplicationCandidateRuntimeReport? report)
    {
        report = null;
        try
        {
            if (operation.Tool != ValidationTool || !operation.Success || operation.Subject != candidate.ApplicationId.Value
                || operation.Id != row.OperationId || row.ApplicationId != candidate.ApplicationId.Value
                || row.CandidateId != candidate.CandidateId || row.Revision != candidate.Revision
                || row.CandidateFingerprint != candidate.ContentFingerprint
                || !TryValidationCommand(operation.ProjectionJson, candidate, out var command) || command is null
                || command.applicationId != candidate.ApplicationId.Value
                || !ValidOriginalIdentity(command.principal, command.authenticationMethod, command.CommandId)
                || command.Samples.Any(sample => !actualDefinitions.Contains(sample.Definition))
                || InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-validation/v1",
                    command.CanonicalCommand) != row.CanonicalCommandFingerprint)
                return false;
            using var guardDocument = JsonDocument.Parse(operation.GuardEvidenceJson);
            if (!guardDocument.RootElement.TryGetProperty("runtimeReport", out var reportElement)
                || !guardDocument.RootElement.TryGetProperty("runtimeReportFingerprint", out var fingerprintElement)
                || fingerprintElement.ValueKind != JsonValueKind.String)
                return false;
            if (!TryParseRuntimeReport(reportElement, candidate, out var parsed) || parsed is null) return false;
            var request = new ApplicationCandidateValidationRequest(candidate, command.Samples);
            var preparationVersion = parsed is { RuntimePolicyVersion: not null, RuntimePolicyFingerprint: not null }
                ? parsed.RuntimePolicyVersion + "@" + parsed.RuntimePolicyFingerprint : null;
            if (!TryValidateRuntimeReport(parsed, request, out _, out var reportFingerprint)
                || parsed.Status == ApplicationCandidateRuntimeStatus.Invalid && row.Outcome != "invalid"
                || parsed.Status == ApplicationCandidateRuntimeStatus.Unavailable && row.Outcome != "unavailable"
                || parsed.Status == ApplicationCandidateRuntimeStatus.Completed
                    && row.Outcome is not ("unavailable" or "valid")
                || fingerprintElement.GetString() != reportFingerprint
                || row.PreparedEvidenceReference != RuntimeEvidenceReference(operation.Id, reportFingerprint)
                || row.PreparationVersion != preparationVersion
                || operation.GuardEvidenceJson != Guard(command.principal!, command.authenticationMethod!,
                    command.applicationId!, command.CommandId!, candidate, row.GrantReference, actualDefinitions,
                    row.CanonicalCommandFingerprint, ValidationFingerprint(row), parsed))
                return false;
            report = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static bool TryParseRuntimeReport(JsonElement element, ApplicationCandidateReference candidate,
        out ApplicationCandidateRuntimeReport? report)
    {
        report = null;
        try
        {
            if (element.ValueKind != JsonValueKind.Object
                || InteractionCanonicalJson.CanonicalizeObject(element.GetProperty("Candidate").GetRawText())
                    != InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(candidate))
                || !NullableString(element, "SelectionEvidenceFingerprint", out var selectionFingerprint)
                || !NullableString(element, "RuntimePolicyVersion", out var runtimePolicyVersion)
                || !NullableString(element, "RuntimePolicyFingerprint", out var runtimePolicyFingerprint)
                || element.GetProperty("Samples").ValueKind != JsonValueKind.Array
                || element.GetProperty("Diagnostics").ValueKind != JsonValueKind.Array)
                return false;
            var status = (ApplicationCandidateRuntimeStatus)element.GetProperty("Status").GetInt32();
            var samples = new List<ApplicationCandidateRuntimeSampleResult>();
            foreach (var value in element.GetProperty("Samples").EnumerateArray())
            {
                var definitionElement = value.GetProperty("Definition");
                var definition = new StandingGrantDefinitionReference(
                    definitionElement.GetProperty("DefinitionId").GetString()!,
                    definitionElement.GetProperty("Kind").GetString()!,
                    definitionElement.GetProperty("Revision").GetInt32(),
                    definitionElement.GetProperty("ContentFingerprint").GetString()!);
                if (!NullableString(value, "ActualDataFingerprint", out var actualFingerprint)) return false;
                samples.Add(new(definition, value.GetProperty("SampleIndex").GetInt32(),
                    (ApplicationCandidateRuntimeStatus)value.GetProperty("Outcome").GetInt32(),
                    value.GetProperty("Attempted").GetBoolean(),
                    value.GetProperty("InputFingerprint").GetString()!,
                    value.GetProperty("ExpectedDataFingerprint").GetString()!, actualFingerprint));
            }
            var diagnostics = new List<ApplicationCandidateDiagnostic>();
            foreach (var value in element.GetProperty("Diagnostics").EnumerateArray())
                diagnostics.Add(new(value.GetProperty("Code").GetString()!,
                    value.GetProperty("Target").GetString()!, value.GetProperty("Message").GetString()!));
            report = new(status, candidate, selectionFingerprint, runtimePolicyVersion,
                runtimePolicyFingerprint, samples.AsReadOnly(), diagnostics.AsReadOnly());
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static bool NullableString(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Null) return true;
        if (property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return value is not null;
    }

    private static bool TryWriteCommand(string json, out ApplicationCandidateOriginalCommand? command)
    {
        command = null;
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > 64 * 1024 || InteractionCanonicalJson.CanonicalizeObject(json) != json) return false;
            var parsed = JsonSerializer.Deserialize<WriteCommand>(json);
            if (parsed is null || parsed.principal is null || parsed.authenticationMethod is null || parsed.applicationId is null
                || parsed.CommandId is null || parsed.candidateId is null || parsed.request is null) return false;
            SqliteApplicationAuthoringService.Validate(parsed.request);
            var canonical = CanonicalWrite(parsed.principal, parsed.authenticationMethod, parsed.applicationId,
                parsed.CommandId, parsed.request, parsed.candidateId);
            if (canonical != json) return false;
            command = new(parsed.principal, parsed.authenticationMethod, parsed.applicationId, parsed.CommandId,
                parsed.candidateId, parsed.request, canonical);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException or ApplicationActivationException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static bool TryValidationCommand(string json, ApplicationCandidateReference expectedCandidate,
        out ValidationCommand? command)
    {
        command = null;
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > 64 * 1024 || InteractionCanonicalJson.CanonicalizeObject(json) != json) return false;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !String(root, "principal", out var principal)
                || !String(root, "authenticationMethod", out var authenticationMethod)
                || !String(root, "applicationId", out var applicationId)
                || !String(root, "CommandId", out var commandId)
                || !root.TryGetProperty("candidate", out _)
                || !root.TryGetProperty("samples", out var sampleElement)) return false;
            var samples = sampleElement.Deserialize<ApplicationCandidateValidationSample[]>()
                ?? throw new JsonException("Retained samples are absent.");
            var normalized = SqliteApplicationAuthoringService.NormalizeSamples(samples);
            var canonical = CanonicalValidation(principal, authenticationMethod, applicationId, commandId, expectedCandidate, normalized);
            if (canonical != json) return false;
            command = new(principal, authenticationMethod, applicationId, commandId, canonical, normalized);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException or ApplicationActivationException or ArgumentException
            or InvalidOperationException or NotSupportedException) { return false; }
    }

    private static bool DocumentsMatch(ApplicationCandidateWriteRequest request, IReadOnlyList<ActivatedApplicationDocument> documents)
    {
        if (request.Documents is null || request.Documents.Count == 0) return false;
        foreach (var input in request.Documents)
        {
            if (input is null) return false;
            var bytes = Encoding.UTF8.GetBytes(input.Text);
            var match = documents.SingleOrDefault(document => document.RelativePath == input.RelativePath);
            if (match is null || match.LogicalIdentity != input.LogicalIdentity || match.SourceId != input.SourceId
                || match.MediaType != input.MediaType || !match.IsText || match.Length != bytes.LongLength
                || match.ContentFingerprint != Convert.ToHexString(SHA256.HashData(bytes))) return false;
        }
        return request.Documents.Select(value => value.RelativePath).Distinct(StringComparer.Ordinal).Count() == request.Documents.Count;
    }

    private static string CanonicalWrite(string principal, string authenticationMethod, string applicationId,
        string commandId, ApplicationCandidateWriteRequest request, string candidateId) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { principal, authenticationMethod, applicationId,
            CommandId = commandId, candidateId, request }));

    private static string CanonicalValidation(string principal, string authenticationMethod, string applicationId,
        string commandId, ApplicationCandidateReference candidate, IReadOnlyList<ApplicationCandidateValidationSample> samples) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { principal, authenticationMethod, applicationId,
            CommandId = commandId, candidate, samples }));

    private static string Guard(string principal, string authenticationMethod, string applicationId, string commandId,
        ApplicationCandidateReference candidate, string grantReference, IReadOnlyList<StandingGrantDefinitionReference> definitions,
        string commandFingerprint, string? validationFingerprint, ApplicationCandidateRuntimeReport? runtimeReport = null)
    {
        var authorization = new AuthorizationAuditEvidence(principal, authenticationMethod, "standing-grant", applicationId,
            commandId, true, "STANDING_GRANT_ALLOWED");
        var selectedDefinitions = definitions.OrderBy(value => value.DefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.Kind, StringComparer.Ordinal).ThenBy(value => value.Revision)
            .ThenBy(value => value.ContentFingerprint, StringComparer.Ordinal)
            .Select(value => new { value.DefinitionId, value.Kind, value.Revision, value.ContentFingerprint }).ToArray();
        if (validationFingerprint is null)
            return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            { authorization, commandFingerprint, candidate, grantReference, selectedDefinitions }));
        if (runtimeReport is null)
            return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            { authorization, commandFingerprint, candidate, grantReference, selectedDefinitions, validationFingerprint }));
        var runtimeReportFingerprint = RuntimeReportFingerprint(runtimeReport);
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        { authorization, commandFingerprint, candidate, grantReference, selectedDefinitions, validationFingerprint,
            runtimeReportFingerprint, runtimeReport }));
    }

    private static string RuntimeDataFingerprint(string json) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-runtime-data/v1",
            InteractionCanonicalJson.CanonicalizeObject(json));

    private static bool Hash(string value) => value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private static bool ValidOriginalIdentity(string? principal, string? method, string? commandId)
    {
        if (!TrustedPrincipalContext.IsValidPrincipalId(principal) || string.IsNullOrWhiteSpace(method)
            || method.Length > 64 || commandId is null) return false;
        return commandId.Length is > 0 and <= InteractionContractLimits.IdempotencyKey
            && commandId.All(value => char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or ':' or '-');
    }

    private static bool String(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        return parent.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } parsed && (value = parsed) is not null;
    }

    private sealed record WriteCommand(string? principal, string? authenticationMethod, string? applicationId,
        string? CommandId, string? candidateId, ApplicationCandidateWriteRequest? request);
    private sealed record ValidationCommand(string? principal, string? authenticationMethod, string? applicationId,
        string? CommandId, string CanonicalCommand,
        IReadOnlyList<ApplicationCandidateValidationSample> Samples);
}
