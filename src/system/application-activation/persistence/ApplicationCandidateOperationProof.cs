using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
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
        ApplicationCandidateValidationRecord row, IReadOnlyList<StandingGrantDefinitionReference> definitions, string commandFingerprint) =>
        Guard(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, candidate.ApplicationId.Value,
            host.CommandId, candidate, row.GrantReference, definitions, commandFingerprint, ValidationFingerprint(row));

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
        return operation.GuardEvidenceJson == Guard(principal, authenticationMethod, applicationId,
            commandId, candidate, row.GrantReference, actualDefinitions, row.CanonicalCommandFingerprint, ValidationFingerprint(row));
    }

    internal static string ValidationFingerprint(ApplicationCandidateValidationRecord row) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-validation-outcome/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(row)));

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
            command = new(principal, authenticationMethod, applicationId, commandId, canonical);
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
        string commandFingerprint, string? validationFingerprint)
    {
        var authorization = new AuthorizationAuditEvidence(principal, authenticationMethod, "standing-grant", applicationId,
            commandId, true, "STANDING_GRANT_ALLOWED");
        var selectedDefinitions = definitions.OrderBy(value => value.DefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.Kind, StringComparer.Ordinal).ThenBy(value => value.Revision)
            .ThenBy(value => value.ContentFingerprint, StringComparer.Ordinal)
            .Select(value => new { value.DefinitionId, value.Kind, value.Revision, value.ContentFingerprint }).ToArray();
        return validationFingerprint is null
            ? InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            { authorization, commandFingerprint, candidate, grantReference, selectedDefinitions }))
            : InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            { authorization, commandFingerprint, candidate, grantReference, selectedDefinitions, validationFingerprint }));
    }

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
        string? CommandId, string CanonicalCommand = "");
}
