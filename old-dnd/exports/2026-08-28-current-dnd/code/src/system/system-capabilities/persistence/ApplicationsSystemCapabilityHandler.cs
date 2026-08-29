using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.StateSpaceAdministration;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>Composes application-owned metadata without becoming its persistence authority.</summary>
public sealed class ApplicationsSystemCapabilityHandler(
    IApplicationRegistry applications,
    IApplicationActivationReader? activations = null,
    IStateSpaceAdministrationReader? stateSpaces = null) : ISystemReadCapabilityHandler
{
    private const string Recovery = "Inspect registered applications and retry with a valid application ID and limit.";
    private readonly IApplicationRegistry _applications = applications;
    private readonly IApplicationActivationReader? _activations = activations;
    private readonly IStateSpaceAdministrationReader? _stateSpaces = stateSpaces;

    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.Applications,
        1,
        "application-registry",
        "Authenticated bounded inspection of registered applications, current activation summaries, and state-space bindings.",
        SystemCapabilityMode.Read,
        InputSchema,
        OutputSchema,
        ["procedure.system.inspect"],
        PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.PrivateOperatorMetadata,
        RequiresConfirmation: false,
        RequiresIdempotencyKey: false);

    public Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var applicationId = NullableString(input, "applicationId");
        var afterApplicationId = NullableString(input, "afterApplicationId");
        var limit = input.GetProperty("limit").GetInt32();
        if (applicationId is not null && afterApplicationId is not null)
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "SYSTEM_CAPABILITY_INPUT_INVALID",
                "applicationId and afterApplicationId cannot be supplied together.",
                Recovery));

        try
        {
            return Task.FromResult(applicationId is null
                ? List(afterApplicationId, limit)
                : Exact(applicationId, limit));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "SYSTEM_CAPABILITY_INPUT_INVALID", "The application capability input is invalid.", Recovery));
        }
        catch (InvalidOperationException exception) when (exception.Message == "CURSOR_STALE")
        {
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "CURSOR_STALE", "The application continuation no longer matches current registry state.",
                "Restart application discovery without a cursor."));
        }
    }

    private SystemCapabilityHandlerResult List(string? afterApplicationId, int limit)
    {
        var page = _applications.ListPage(afterApplicationId, limit);
        var values = page.Applications.Select(value => View(value, _applications.Get(value.Id)!)).ToArray();
        return SystemCapabilityHandlerResult.Success(JsonSerializer.SerializeToElement(new
        {
            applications = values,
            application = (object?)null,
            stateSpaces = Array.Empty<object>(),
            nextApplicationId = page.NextApplicationId,
            limit
        }));
    }

    private SystemCapabilityHandlerResult Exact(string applicationId, int limit)
    {
        var id = ApplicationIdentifier.Parse(applicationId);
        var registration = _applications.Describe(id);
        var revision = _applications.Get(id);
        if (registration is null || revision is null)
            return SystemCapabilityHandlerResult.Failure(
                "APPLICATION_UNKNOWN", "The requested application is not registered.",
                "List registered applications and choose an exact application ID.");
        var spaces = _stateSpaces?.List(id, limit).Select(StateSpace).ToArray() ?? [];
        return SystemCapabilityHandlerResult.Success(JsonSerializer.SerializeToElement(new
        {
            applications = Array.Empty<object>(),
            application = View(registration, revision),
            stateSpaces = spaces,
            nextApplicationId = (string?)null,
            limit
        }));
    }

    private object View(ApplicationRegistration registration, ApplicationRevision revision) => new
    {
        id = registration.Id.Value,
        displayName = registration.DisplayName,
        description = registration.Description,
        revision = revision.Revision,
        fingerprint = revision.Fingerprint,
        baseApplications = registration.BaseApplications.Select(value => value.Value).ToArray(),
        active = _activations?.Current(registration.Id) is { } active ? Activation(active) : null
    };

    private static object Activation(ActiveApplicationManifest value) => new
    {
        applicationId = value.ApplicationId.Value,
        activationRevision = value.ActivationRevision,
        applicationRevision = value.ApplicationRevision,
        applicationFingerprint = value.ApplicationFingerprint,
        previewFingerprint = value.PreviewFingerprint,
        scannedDocumentsFingerprint = value.ScannedDocumentsFingerprint,
        candidateManifestFingerprint = value.CandidateManifestFingerprint,
        dependencyGraphFingerprint = value.DependencyGraphFingerprint,
        activationFingerprint = value.ActivationFingerprint,
        dependencyCoverageVersion = value.DependencyCoverageVersion,
        dependencyCoverageComplete = value.DependencyCoverageComplete,
        sourceCount = value.Sources.Count,
        sourceIds = value.Sources.Select(source => source.SourceId).ToArray(),
        winnerCount = value.Winners.Count,
        activatedAtUtc = value.ActivatedAtUtc
    };

    private static object StateSpace(StateSpaceBindingSummary value) => new
    {
        stateSpaceId = value.StateSpaceId,
        applicationId = value.ApplicationId.Value,
        applicationRevision = value.ApplicationRevision,
        applicationFingerprint = value.ApplicationFingerprint,
        activeFingerprint = value.ActiveFingerprint,
        bindingRevision = value.BindingRevision,
        bindingFingerprint = value.BindingFingerprint,
        createdAtUtc = value.CreatedAtUtc,
        updatedAtUtc = value.UpdatedAtUtc
    };

    private static string? NullableString(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.GetString();
    }

    private const string InputSchema = """
    {
      "$schema":"https://json-schema.org/draft/2020-12/schema",
      "type":"object",
      "additionalProperties":false,
      "required":["limit"],
      "properties":{
        "applicationId":{"anyOf":[{"type":"string","minLength":1,"maxLength":63},{"type":"null"}]},
        "afterApplicationId":{"anyOf":[{"type":"string","minLength":1,"maxLength":63},{"type":"null"}]},
        "limit":{"type":"integer","minimum":1,"maximum":100}
      }
    }
    """;

    private const string OutputSchema = """
    {
      "$schema":"https://json-schema.org/draft/2020-12/schema",
      "$defs":{
        "activation":{
          "type":"object","additionalProperties":false,
          "required":["applicationId","activationRevision","applicationRevision","applicationFingerprint","previewFingerprint","scannedDocumentsFingerprint","candidateManifestFingerprint","dependencyGraphFingerprint","activationFingerprint","dependencyCoverageVersion","dependencyCoverageComplete","sourceCount","sourceIds","winnerCount","activatedAtUtc"],
          "properties":{
            "applicationId":{"type":"string","minLength":1,"maxLength":63},
            "activationRevision":{"type":"integer","minimum":1},
            "applicationRevision":{"type":"integer","minimum":1},
            "applicationFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "previewFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "scannedDocumentsFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "candidateManifestFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "dependencyGraphFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "activationFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "dependencyCoverageVersion":{"type":"string","minLength":1,"maxLength":120},
            "dependencyCoverageComplete":{"type":"boolean"},
            "sourceCount":{"type":"integer","minimum":0},
            "sourceIds":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}},
            "winnerCount":{"type":"integer","minimum":0},
            "activatedAtUtc":{"type":"string","format":"date-time"}
          }
        },
        "application":{
          "type":"object","additionalProperties":false,
          "required":["id","displayName","description","revision","fingerprint","baseApplications","active"],
          "properties":{
            "id":{"type":"string","minLength":1,"maxLength":63},
            "displayName":{"type":"string","minLength":1,"maxLength":500},
            "description":{"type":"string","maxLength":4000},
            "revision":{"type":"integer","minimum":1},
            "fingerprint":{"type":"string","minLength":64,"maxLength":64},
            "baseApplications":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":63}},
            "active":{"anyOf":[{"$ref":"#/$defs/activation"},{"type":"null"}]}
          }
        },
        "stateSpace":{
          "type":"object","additionalProperties":false,
          "required":["stateSpaceId","applicationId","applicationRevision","applicationFingerprint","activeFingerprint","bindingRevision","bindingFingerprint","createdAtUtc","updatedAtUtc"],
          "properties":{
            "stateSpaceId":{"type":"string","minLength":1,"maxLength":200},
            "applicationId":{"type":"string","minLength":1,"maxLength":63},
            "applicationRevision":{"type":"integer","minimum":1},
            "applicationFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "activeFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "bindingRevision":{"type":"integer","minimum":1},
            "bindingFingerprint":{"type":"string","minLength":64,"maxLength":64},
            "createdAtUtc":{"anyOf":[{"type":"string","format":"date-time"},{"type":"null"}]},
            "updatedAtUtc":{"anyOf":[{"type":"string","format":"date-time"},{"type":"null"}]}
          }
        }
      },
      "type":"object","additionalProperties":false,
      "required":["applications","application","stateSpaces","nextApplicationId","limit"],
      "properties":{
        "applications":{"type":"array","maxItems":100,"items":{"$ref":"#/$defs/application"}},
        "application":{"anyOf":[{"$ref":"#/$defs/application"},{"type":"null"}]},
        "stateSpaces":{"type":"array","maxItems":100,"items":{"$ref":"#/$defs/stateSpace"}},
        "nextApplicationId":{"anyOf":[{"type":"string","minLength":1,"maxLength":63},{"type":"null"}]},
        "limit":{"type":"integer","minimum":1,"maximum":100}
      }
    }
    """;
}
