using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemCapabilities;

internal sealed class ApplicationCandidateIntentUpdateCapabilityHandler(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IntentMatchAssociationService associations) : ISystemWriteCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.ApplicationCandidateIntentUpdate,
        1,
        "interaction-orchestration",
        "Stage an inert Matches-only candidate for one exact current procedure or mechanic; the selected standing grant must carry both Read and Author authority.",
        SystemCapabilityMode.Write,
        InputSchema,
        ApplicationCandidateCapabilitySchemas.WriteOutput,
        ["procedure.system.application-candidate.intent-update"],
        PrivateOperatorCapability.Modify,
        SystemCapabilitySensitivity.PrivateOperatorMetadata,
        true,
        true);

    public Task<SystemCapabilityWritePreflight> PreflightAsync(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var parsed = Parse(input);
            var fingerprint = ApplicationCandidateCapabilitySchemas.Fingerprint(input);
            return Task.FromResult(SystemCapabilityWritePreflight.Ready(
                fingerprint,
                $"Stage alternate intent phrases for exact {parsed.Request.Target.Kind} '{parsed.Request.Target.DefinitionId}'.",
                [$"application:{parsed.ApplicationId}",
                    $"{parsed.Request.Target.Kind}:{parsed.Request.Target.DefinitionId}@{parsed.Request.Target.Revision}"],
                JsonSerializer.Serialize(new { inputFingerprint = fingerprint })));
        }
        catch (InteractionContractException exception)
        {
            return Task.FromResult(SystemCapabilityWritePreflight.Failure(
                exception.Code,
                "The intent association request is invalid.",
                ApplicationCandidateCapabilitySchemas.Recovery));
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return Task.FromResult(SystemCapabilityWritePreflight.Failure(
                "INVALID_PAYLOAD",
                "The intent association request is invalid.",
                ApplicationCandidateCapabilitySchemas.Recovery));
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
        JsonElement input,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = Parse(input);
            if (!ApplicationCandidateCapabilitySchemas.MatchesEvidence(
                    context.ExecutionEvidenceJson,
                    ApplicationCandidateCapabilitySchemas.Fingerprint(input)))
                return Failure("SYSTEM_CAPABILITY_PREFLIGHT_STALE");
            var applicationId = ApplicationIdentifier.Parse(parsed.ApplicationId);
            var selection = await ApplicationCandidateCapabilityHost.CreateAsync(
                db,
                applications,
                context.Invocation,
                applicationId,
                [StandingGrantCapability.Read, StandingGrantCapability.Author],
                context.RequestToken,
                InteractionExecutionProfile.Atomic,
                2,
                cancellationToken);
            if (selection.Hosts.Count == 0) return Failure(selection.Code);

            InteractionInvocationResult? result = null;
            foreach (var host in selection.Hosts)
            {
                result = await associations.WriteAsync(host, parsed.Request, cancellationToken);
                if (result.Tag == InteractionInvocationResultTag.Committed && result.Receipt is not null)
                    return SystemCapabilityWriteHandlerResult.Success(
                        JsonSerializer.SerializeToElement(new
                        {
                            status = result.WireTag,
                            code = result.Code,
                            message = result.SafeMessage,
                            operationId = result.Receipt.OperationId,
                            requestFingerprint = result.Receipt.RequestFingerprint
                        }),
                        result.Receipt.OperationId,
                        result.Receipt.RequestFingerprint);
                if (!ApplicationCandidateCapabilityHost.RetryableGrantDenial(result.Code)) break;
            }
            return Failure(result?.Code ?? selection.Code, result?.SafeMessage);
        }
        catch (OperationCanceledException) { throw; }
        catch (InteractionContractException exception)
        {
            return Failure(exception.Code, "The intent association request is invalid.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return Failure("INVALID_PAYLOAD");
        }
    }

    private static Parsed Parse(JsonElement input)
    {
        var value = ApplicationCandidateCapabilitySchemas.Deserialize<IntentUpdateWire>(input);
        _ = ApplicationIdentifier.Parse(value.ApplicationId);
        if ((value.CandidateId is null) != (value.ExpectedCandidateRevision == 0)
            || value.ExpectedCandidateRevision < 0
            || value.Target.Revision < 1
            || value.Target.Kind is not ("procedure" or "mechanic")
            || !ValidHash(value.Target.ContentFingerprint)
            || value.MatchPhrases is null || value.MatchPhrases.Count > 32)
            throw new InteractionContractException("INTENT_ASSOCIATION_INVALID",
                "The intent association request is invalid.");
        var phrases = value.MatchPhrases.Select(Normalize).ToArray();
        if (phrases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != phrases.Length)
            throw new InteractionContractException("INTENT_ASSOCIATION_INVALID",
                "Intent phrases must be unique after normalization.");
        var target = new StandingGrantDefinitionReference(value.Target.DefinitionId,
            value.Target.Kind, value.Target.Revision, value.Target.ContentFingerprint);
        return new(value.ApplicationId, new(value.CandidateId,
            value.ExpectedCandidateRevision, target, Array.AsReadOnly(phrases)));
    }

    private static string Normalize(string value)
    {
        if (value is null || value.Any(char.IsControl))
            throw new InteractionContractException("INTENT_ASSOCIATION_INVALID",
                "Intent phrases are invalid.");
        var result = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (result.Length is 0 or > 200)
            throw new InteractionContractException("INTENT_ASSOCIATION_INVALID",
                "Intent phrases are invalid.");
        return result;
    }

    private static bool ValidHash(string value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static SystemCapabilityWriteHandlerResult Failure(string code, string? message = null) =>
        SystemCapabilityWriteHandlerResult.Failure(code,
            message ?? "The intent association request was rejected.",
            ApplicationCandidateCapabilitySchemas.Recovery);

    private sealed record Parsed(string ApplicationId, IntentMatchAssociationWriteRequest Request);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record IntentUpdateWire(
        [property: JsonRequired] string ApplicationId,
        [property: JsonRequired] string? CandidateId,
        [property: JsonRequired] int ExpectedCandidateRevision,
        [property: JsonRequired] TargetWire Target,
        [property: JsonRequired] IReadOnlyList<string> MatchPhrases);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record TargetWire(
        [property: JsonRequired] string DefinitionId,
        [property: JsonRequired] string Kind,
        [property: JsonRequired] int Revision,
        [property: JsonRequired] string ContentFingerprint);

    internal const string InputSchema = """
        {
          "$schema":"https://json-schema.org/draft/2020-12/schema",
          "type":"object",
          "additionalProperties":false,
          "required":["applicationId","candidateId","expectedCandidateRevision","target","matchPhrases"],
          "properties":{
            "applicationId":{"type":"string","minLength":1,"maxLength":63},
            "candidateId":{"anyOf":[{"type":"string","minLength":32,"maxLength":32},{"type":"null"}]},
            "expectedCandidateRevision":{"type":"integer","minimum":0},
            "target":{
              "type":"object",
              "additionalProperties":false,
              "required":["definitionId","kind","revision","contentFingerprint"],
              "properties":{
                "definitionId":{"type":"string","minLength":1,"maxLength":200},
                "kind":{"type":"string","enum":["procedure","mechanic"]},
                "revision":{"type":"integer","minimum":1},
                "contentFingerprint":{"type":"string","minLength":64,"maxLength":64}
              }
            },
            "matchPhrases":{
              "type":"array",
              "maxItems":32,
              "items":{"type":"string","minLength":1,"maxLength":200},
              "uniqueItems":true
            }
          }
        }
        """;
}
