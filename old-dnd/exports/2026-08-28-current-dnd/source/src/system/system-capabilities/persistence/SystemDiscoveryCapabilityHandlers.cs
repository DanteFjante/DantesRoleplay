using System.Text.Json;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;

namespace DantesRoleplay.SystemCapabilities;

internal static class SystemCapabilityJson
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    internal static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, Web);
}

public sealed class SourcesSystemCapabilityHandler(
    IApplicationRegistry applications,
    ISourceRegistry sources,
    ISourceScanReceiptStore scans) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.Sources, 1, "source-registry",
        "Inspect immutable source registrations and latest scan evidence for one registered application.",
        SystemCapabilityMode.Read, InputSchema, OutputSchema,
        ["procedure.system.inspect", "procedure.system.use"],
        PrivateOperatorCapability.Read, SystemCapabilitySensitivity.PrivateOperatorMetadata,
        false, false);

    public Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        ApplicationIdentifier applicationId;
        try { applicationId = ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!); }
        catch (ArgumentException)
        {
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "INVALID_APPLICATION", "The application ID is invalid.", "Inspect system.applications and retry."));
        }
        if (applications.Get(applicationId) is null)
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "APPLICATION_UNKNOWN", "The application is not registered.", "Inspect system.applications and retry."));
        var limit = input.TryGetProperty("limit", out var limitElement) ? limitElement.GetInt32() : 50;
        var sourceId = input.TryGetProperty("sourceId", out var sourceElement)
            ? sourceElement.GetString() : null;
        var registrations = sourceId is null
            ? sources.List(applicationId, limit)
            : sources.Get(applicationId, sourceId) is { } exact ? [exact] : [];
        if (sourceId is not null && registrations.Count == 0)
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "SOURCE_UNKNOWN", "The source is not registered.", "List the application's registered sources and retry."));
        var values = registrations.Select(value => Describe(value, scans.Latest(applicationId, value.SourceId))).ToArray();
        return Task.FromResult(SystemCapabilityHandlerResult.Success(SystemCapabilityJson.Element(new
        {
            applicationId = applicationId.Value,
            sources = values,
            source = sourceId is null ? null : values.Single(),
            limit
        })));
    }

    private static object Describe(SourceRegistration value, SourceScanReceipt? scan) => new
    {
        applicationId = value.ApplicationId.Value,
        value.SourceId,
        value.AllowedRootId,
        value.RelativePathOrGlob,
        trust = value.Trust.ToString().ToLowerInvariant(),
        value.Precedence,
        value.LogicalIdentity,
        fingerprint = SourceRegistrationFingerprint.Compute(value),
        latestScan = scan is null ? null : new
        {
            scan.Generation,
            status = scan.Status.ToString().ToLowerInvariant(),
            scan.ContentFingerprint,
            scan.RecordedAtUtc
        }
    };

    private const string InputSchema = """
    {
      "type":"object","additionalProperties":false,"required":["applicationId"],
      "properties":{
        "applicationId":{"type":"string","minLength":1,"maxLength":63},
        "sourceId":{"type":"string","minLength":1,"maxLength":200},
        "limit":{"type":"integer","minimum":1,"maximum":100}
      }
    }
    """;

    private const string OutputSchema = """
    {
      "$defs":{
        "scan":{"type":"object","additionalProperties":false,"required":["generation","status","contentFingerprint","recordedAtUtc"],"properties":{
          "generation":{"type":"integer","minimum":1},"status":{"enum":["succeeded","failed"]},
          "contentFingerprint":{"type":"string","minLength":64,"maxLength":64},"recordedAtUtc":{"type":"string","format":"date-time"}}},
        "source":{"type":"object","additionalProperties":false,"required":["applicationId","sourceId","allowedRootId","relativePathOrGlob","trust","precedence","logicalIdentity","fingerprint","latestScan"],"properties":{
          "applicationId":{"type":"string","minLength":1,"maxLength":63},"sourceId":{"type":"string","minLength":1,"maxLength":200},
          "allowedRootId":{"type":"string","minLength":1,"maxLength":200},"relativePathOrGlob":{"type":"string","minLength":1,"maxLength":1000},
          "trust":{"enum":["trusted","untrusted"]},"precedence":{"type":"integer","minimum":-1000000,"maximum":1000000},
          "logicalIdentity":{"type":"string","minLength":1,"maxLength":200},"fingerprint":{"type":"string","minLength":64,"maxLength":64},
          "latestScan":{"anyOf":[{"$ref":"#/$defs/scan"},{"type":"null"}]}}}
      },
      "type":"object","additionalProperties":false,"required":["applicationId","sources","source","limit"],"properties":{
        "applicationId":{"type":"string","minLength":1,"maxLength":63},"sources":{"type":"array","maxItems":100,"items":{"$ref":"#/$defs/source"}},
        "source":{"anyOf":[{"$ref":"#/$defs/source"},{"type":"null"}]},"limit":{"type":"integer","minimum":1,"maximum":100}}
    }
    """;
}

public sealed class ApplicationPreviewSystemCapabilityHandler(
    IApplicationPreviewService previews) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.ApplicationPreview, 1, "application-preview",
        "Build a disposable safe summary of the current registered source overlay for one application.",
        SystemCapabilityMode.Read, InputSchema, OutputSchema,
        ["procedure.system.use"], PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false);

    public async Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var applicationId = ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!);
            var sourceIds = input.TryGetProperty("sourceIds", out var selected)
                ? selected.EnumerateArray().Select(value => value.GetString()!).ToArray()
                : null;
            var value = sourceIds is null
                ? await previews.PreviewAsync(applicationId, cancellationToken)
                : await previews.PreviewAsync(applicationId, sourceIds, cancellationToken);
            return SystemCapabilityHandlerResult.Success(SystemCapabilityJson.Element(new
            {
                applicationId = value.ApplicationId.Value,
                value.ApplicationRevision,
                value.ApplicationFingerprint,
                value.ScannedDocumentsFingerprint,
                value.CandidateManifestFingerprint,
                value.PreviewFingerprint,
                value.IsValid,
                sources = value.Sources,
                winnerCount = value.Winners.Count,
                shadowCount = value.Shadows.Count,
                problems = value.Problems.Select(problem => new
                {
                    problem.Code,
                    problem.SourceId,
                    problem.Message
                }).Take(100).ToArray()
            }));
        }
        catch (ArgumentException)
        {
            return SystemCapabilityHandlerResult.Failure(
                "INVALID_APPLICATION", "The application ID is invalid.", "Inspect system.applications and retry.");
        }
        catch (ApplicationPreviewException exception)
        {
            return SystemCapabilityHandlerResult.Failure(exception.Code,
                Safe(exception.Message), "Inspect system.sources and retry after correcting source problems.");
        }
    }

    private static string Safe(string value) => value.Length <= 300 && !value.Any(char.IsControl)
        ? value : "The application preview is unavailable.";

    private const string InputSchema = """
    {"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"sourceIds":{"type":"array","minItems":1,"maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}}}}
    """;

    private const string OutputSchema = """
    {
      "type":"object","additionalProperties":false,
      "required":["applicationId","applicationRevision","applicationFingerprint","scannedDocumentsFingerprint","candidateManifestFingerprint","previewFingerprint","isValid","sources","winnerCount","shadowCount","problems"],
      "properties":{
        "applicationId":{"type":"string","minLength":1,"maxLength":63},"applicationRevision":{"type":"integer","minimum":1},
        "applicationFingerprint":{"type":"string","minLength":64,"maxLength":64},"scannedDocumentsFingerprint":{"type":"string","minLength":64,"maxLength":64},
        "candidateManifestFingerprint":{"type":"string","minLength":64,"maxLength":64},"previewFingerprint":{"type":"string","minLength":64,"maxLength":64},
        "isValid":{"type":"boolean"},
        "sources":{"type":"array","maxItems":100,"items":{"type":"object","additionalProperties":false,"required":["sourceId","registrationFingerprint","documentCount","problemCount"],"properties":{
          "sourceId":{"type":"string","minLength":1,"maxLength":200},"registrationFingerprint":{"type":"string","minLength":64,"maxLength":64},
          "documentCount":{"type":"integer","minimum":0},"problemCount":{"type":"integer","minimum":0}}}},
        "winnerCount":{"type":"integer","minimum":0},"shadowCount":{"type":"integer","minimum":0},
        "problems":{"type":"array","maxItems":100,"items":{"type":"object","additionalProperties":false,"required":["code","sourceId","message"],"properties":{
          "code":{"type":"string","minLength":1,"maxLength":100},"sourceId":{"type":"string","maxLength":200},"message":{"type":"string","minLength":1,"maxLength":500}}}}
      }
    }
    """;
}

public sealed class DependenciesSystemCapabilityHandler(
    IProjectionImpactService impacts) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.Dependencies, 1, "projection-materialization",
        "Inspect declared component-field and projection dependency impact for one application.",
        SystemCapabilityMode.Read, InputSchema, OutputSchema,
        ["procedure.system.inspect"], PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false);

    public Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var app = ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!);
            var id = input.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var transitive = !input.TryGetProperty("transitive", out var transitiveElement) || transitiveElement.GetBoolean();
            var limit = input.TryGetProperty("limit", out var limitElement) ? limitElement.GetInt32() : 100;
            var value = impacts.Analyze(app, id, transitive);
            var inventory = value.Root is null;
            return Task.FromResult(SystemCapabilityHandlerResult.Success(SystemCapabilityJson.Element(new
            {
                applicationId = app.Value,
                value.GraphFingerprint,
                value.Root,
                value.Transitive,
                coverage = new { indexed = new[] { "component-field", "projection" }, complete = false },
                nodes = inventory ? value.Nodes.Take(limit).ToArray() : [],
                edges = inventory ? value.Edges.Take(limit).ToArray() : [],
                dependents = value.Dependents.Take(limit).ToArray(),
                limit,
                truncated = inventory
                    ? value.Nodes.Count > limit || value.Edges.Count > limit
                    : value.Dependents.Count > limit
            })));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "INVALID_APPLICATION", "The application ID is invalid.", "Inspect system.applications and retry."));
        }
        catch (ProjectionImpactException exception)
        {
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                exception.Code, exception.Message, "Inspect the application's dependency inventory and retry."));
        }
    }

    private const string InputSchema = """
    {"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{
      "applicationId":{"type":"string","minLength":1,"maxLength":63},"id":{"type":"string","minLength":1,"maxLength":600},
      "transitive":{"type":"boolean"},"limit":{"type":"integer","minimum":1,"maximum":250}}}
    """;

    private const string OutputSchema = """
    {
      "$defs":{
        "node":{"type":"object","additionalProperties":false,"required":["id","kind","qualifiedId","version","contractHash","pointer"],"properties":{
          "id":{"type":"string","minLength":1,"maxLength":600},"kind":{"type":"string","minLength":1,"maxLength":80},"qualifiedId":{"type":"string","minLength":1,"maxLength":400},
          "version":{"type":"integer","minimum":1},"contractHash":{"type":"string","minLength":64,"maxLength":64},"pointer":{"anyOf":[{"type":"string","maxLength":1000},{"type":"null"}]}}},
        "edge":{"type":"object","additionalProperties":false,"required":["dependencyId","consumerId","reason"],"properties":{
          "dependencyId":{"type":"string","minLength":1,"maxLength":600},"consumerId":{"type":"string","minLength":1,"maxLength":600},"reason":{"type":"string","minLength":1,"maxLength":500}}},
        "dependent":{"type":"object","additionalProperties":false,"required":["node","depth","reasons"],"properties":{
          "node":{"$ref":"#/$defs/node"},"depth":{"type":"integer","minimum":1},"reasons":{"type":"array","maxItems":100,"items":{"type":"string","minLength":1,"maxLength":500}}}}
      },
      "type":"object","additionalProperties":false,"required":["applicationId","graphFingerprint","root","transitive","coverage","nodes","edges","dependents","limit","truncated"],"properties":{
        "applicationId":{"type":"string","minLength":1,"maxLength":63},"graphFingerprint":{"type":"string","minLength":64,"maxLength":64},
        "root":{"anyOf":[{"$ref":"#/$defs/node"},{"type":"null"}]},"transitive":{"type":"boolean"},
        "coverage":{"type":"object","additionalProperties":false,"required":["indexed","complete"],"properties":{"indexed":{"type":"array","maxItems":8,"items":{"type":"string"}},"complete":{"type":"boolean"}}},
        "nodes":{"type":"array","maxItems":250,"items":{"$ref":"#/$defs/node"}},"edges":{"type":"array","maxItems":250,"items":{"$ref":"#/$defs/edge"}},
        "dependents":{"type":"array","maxItems":250,"items":{"$ref":"#/$defs/dependent"}},"limit":{"type":"integer","minimum":1,"maximum":250},"truncated":{"type":"boolean"}}
    }
    """;
}
