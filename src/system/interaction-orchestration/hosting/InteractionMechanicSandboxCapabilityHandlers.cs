using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

public sealed class InteractionMechanicSandboxReadCapabilityHandler(
    IInteractionMechanicSandboxService sandbox) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.MechanicSandboxDrafts, 1, "interaction-orchestration",
        "List or inspect expiring application-scoped mechanic sandbox drafts. Drafts are inert SQLite records and cannot activate or write files.",
        SystemCapabilityMode.Read, SandboxSchemas.ReadInput, SandboxSchemas.ReadOutput,
        ["procedure.system.inspect"], PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false);

    public async Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var application = ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!);
            if (input.TryGetProperty("draftId", out var draftId))
            {
                var value = await sandbox.GetAsync(application, draftId.GetString()!, cancellationToken);
                return value is null
                    ? SystemCapabilityHandlerResult.Failure("MECHANIC_SANDBOX_DRAFT_NOT_FOUND",
                        "The mechanic sandbox draft was not found.", "List current application drafts and retry.")
                    : SystemCapabilityHandlerResult.Success(SandboxSchemas.ReadData([value], value));
            }
            var limit = input.TryGetProperty("limit", out var limitValue) ? limitValue.GetInt32() : 20;
            return SystemCapabilityHandlerResult.Success(SandboxSchemas.ReadData(
                await sandbox.ListAsync(application, limit, cancellationToken)));
        }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException)
        {
            return SystemCapabilityHandlerResult.Failure("MECHANIC_SANDBOX_READ_INVALID",
                SandboxSchemas.Safe(exception.Message), "Use an application ID and optional current sandbox draft ID.");
        }
    }
}

public sealed class InteractionMechanicSandboxWriteCapabilityHandler(
    string capabilityId,
    IInteractionMechanicSandboxService sandbox,
    IInteractionMechanicOpportunityStore opportunities) : ISystemWriteCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = capabilityId switch
    {
        SystemCapabilityIds.MechanicSandboxDraft => new(
            capabilityId, 1, "interaction-orchestration",
            "Create or revise one expiring inert mechanic sandbox draft after explicit review of a learned opportunity. Runs catalog, anti-sprawl, and captured-scenario validation without assigning a mechanic ID or writing files.",
            SystemCapabilityMode.Write, SandboxSchemas.DraftInput, SandboxSchemas.DraftOutput,
            ["procedure.system.create-feature"], PrivateOperatorCapability.Modify,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, true, true),
        SystemCapabilityIds.MechanicSandboxPromote => new(
            capabilityId, 1, "interaction-orchestration",
            "Explicitly approve one current validated mechanic sandbox draft for export review. Promotion writes no files, assigns no permanent ID, changes no schema, and activates nothing.",
            SystemCapabilityMode.Write, SandboxSchemas.PromoteInput, SandboxSchemas.PromoteOutput,
            ["procedure.system.create-feature"], PrivateOperatorCapability.Modify,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, true, true),
        _ => throw new InvalidOperationException("Unknown mechanic sandbox capability.")
    };

    public async Task<SystemCapabilityWritePreflight> PreflightAsync(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (capabilityId == SystemCapabilityIds.MechanicSandboxDraft)
            {
                var command = SandboxSchemas.DraftCommand(input);
                var opportunity = (await opportunities.ListAsync(command.ApplicationId, 50, cancellationToken))
                    .SingleOrDefault(value => value.ProposalFingerprint == command.OpportunityProposalFingerprint);
                if (opportunity is null)
                    return SystemCapabilityWritePreflight.Failure("MECHANIC_SANDBOX_OPPORTUNITY_NOT_FOUND",
                        "The reviewed mechanic opportunity is unavailable.", "List current mechanic opportunities and choose an exact proposal fingerprint.");
                var validation = await sandbox.ValidateAsync(command.ApplicationId, command.StateSpaceId,
                    command.Candidate, command.DraftId, cancellationToken);
                var fingerprint = SandboxSchemas.Hash(JsonSerializer.Serialize(new
                {
                    input,
                    validation.Passed,
                    validatedAtUtc = validation.ValidatedAtUtc
                }));
                return SystemCapabilityWritePreflight.Ready(fingerprint,
                    validation.Passed
                        ? "Create or revise an inert mechanic draft whose catalog, anti-sprawl, and focused replay checks currently pass."
                        : "Store an inert mechanic draft with blocking validation results for further revision.",
                    [$"application:{command.ApplicationId.Value}", $"opportunity:{command.OpportunityProposalFingerprint}"]);
            }
            var promotion = SandboxSchemas.PromotionCommand(input);
            var draft = await sandbox.GetAsync(promotion.ApplicationId, promotion.DraftId, cancellationToken);
            if (draft is null || draft.Revision != promotion.ExpectedRevision || draft.Status != "validated"
                || !draft.Validation.Passed)
                return SystemCapabilityWritePreflight.Failure("MECHANIC_SANDBOX_PROMOTION_BLOCKED",
                    "The draft is missing, stale, expired, or not fully validated.",
                    "Inspect the draft, revise it until every required check passes, and retry with its exact revision.");
            var currentValidation = await sandbox.ValidateAsync(promotion.ApplicationId, promotion.StateSpaceId,
                draft.Candidate, draft.DraftId, cancellationToken);
            if (!currentValidation.Passed)
                return SystemCapabilityWritePreflight.Failure("MECHANIC_SANDBOX_PROMOTION_BLOCKED",
                    "The draft no longer passes current catalog, anti-sprawl, or scenario validation.",
                    "Inspect current conflicts, revise the draft, and retry through a fresh confirmed preflight.");
            return SystemCapabilityWritePreflight.Ready(SandboxSchemas.Hash(JsonSerializer.Serialize(input)),
                "Approve the validated inert draft for an explicit export/review synchronization boundary. No file, permanent ID, schema, migration, or activation will be created.",
                [$"application:{promotion.ApplicationId.Value}", $"mechanic-sandbox-draft:{promotion.DraftId}"]);
        }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException or JsonException)
        {
            return SystemCapabilityWritePreflight.Failure("MECHANIC_SANDBOX_INPUT_INVALID",
                SandboxSchemas.Safe(exception.Message), "Use the exact registered mechanic sandbox schema and current fingerprints.");
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
        JsonElement input,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var operationId = "operation.mechanic-sandbox." + Guid.NewGuid().ToString("N");
            var authority = new InteractionMechanicSandboxWriteAuthority(
                context.Invocation.Principal.PrincipalId,
                SandboxSchemas.AuthorizationReference(context.AuthorizationEvidence),
                context.RequestToken, context.Intent, operationId);
            if (capabilityId == SystemCapabilityIds.MechanicSandboxDraft)
            {
                var command = SandboxSchemas.DraftCommand(input);
                SandboxSchemas.RequireScope(context.Invocation, command.ApplicationId, command.StateSpaceId);
                var draft = await sandbox.CreateOrReviseAsync(command, authority, cancellationToken);
                return SystemCapabilityWriteHandlerResult.Success(SandboxSchemas.DraftData(draft), operationId,
                    draft.CandidateFingerprint);
            }
            var promotion = SandboxSchemas.PromotionCommand(input);
            SandboxSchemas.RequireScope(context.Invocation, promotion.ApplicationId, promotion.StateSpaceId);
            var result = await sandbox.PromoteAsync(promotion, authority, cancellationToken);
            return SystemCapabilityWriteHandlerResult.Success(SandboxSchemas.PromotionData(result.Draft, result.Export),
                operationId, result.Draft.CandidateFingerprint);
        }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException or JsonException)
        {
            return SystemCapabilityWriteHandlerResult.Failure("MECHANIC_SANDBOX_WRITE_REJECTED",
                SandboxSchemas.Safe(exception.Message), "Inspect current opportunity and draft state, then retry through a fresh confirmed preflight.");
        }
    }
}

internal static class SandboxSchemas
{
    private const string HashSchema = "{\"type\":\"string\",\"minLength\":64,\"maxLength\":64}";
    private const string CandidateSchema = """
    {"type":"object","additionalProperties":false,"required":["name","category","description","matchPhrases","requirements","source","effectAllowlist","limits","scenarios"],"properties":{
      "name":{"type":"string","minLength":1,"maxLength":200},"category":{"type":"string","minLength":1,"maxLength":200},"description":{"type":"string","minLength":1,"maxLength":2000},
      "matchPhrases":{"type":"array","minItems":1,"maxItems":8,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}},
      "requirements":{"type":"object"},"source":{"type":"string","minLength":1,"maxLength":65536},
      "effectAllowlist":{"type":"object","additionalProperties":false,"required":["effectTypes","componentIds"],"properties":{"effectTypes":{"type":"array","maxItems":9,"uniqueItems":true,"items":{"enum":["entity.create","entity.delete","component.add","component.set","component.merge","component.remove","containment.move","relationship.create","relationship.remove"]}},"componentIds":{"type":"array","maxItems":64,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}}}},
      "limits":{"type":"object","additionalProperties":false,"required":["maxStatements","timeoutMilliseconds","memoryBytes","maxRecursionDepth","maxEffects","maxEvents","maxNotifications","maxLogLines"],"properties":{"maxStatements":{"type":"integer","minimum":1,"maximum":50000},"timeoutMilliseconds":{"type":"integer","minimum":1,"maximum":1000},"memoryBytes":{"type":"integer","minimum":1,"maximum":4194304},"maxRecursionDepth":{"type":"integer","minimum":1,"maximum":32},"maxEffects":{"type":"integer","minimum":0,"maximum":50},"maxEvents":{"type":"integer","minimum":0,"maximum":10},"maxNotifications":{"type":"integer","minimum":0,"maximum":10},"maxLogLines":{"type":"integer","minimum":0,"maximum":50}}},
      "scenarios":{"type":"array","minItems":1,"maxItems":8,"items":{"type":"object","additionalProperties":false,"required":["name","projection","expected"],"properties":{"name":{"type":"string","minLength":1,"maxLength":100},"projection":{"type":"object"},"expected":{"type":"object","additionalProperties":false,"required":["successful","minimumEffects","maximumEffects","effectTypes","componentIds"],"properties":{"successful":{"const":true},"minimumEffects":{"type":"integer","minimum":0,"maximum":50},"maximumEffects":{"type":"integer","minimum":0,"maximum":50},"effectTypes":{"type":"array","maxItems":9,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":100}},"componentIds":{"type":"array","maxItems":64,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}},"narrationContains":{"type":"string","maxLength":500}}}}}}
    }}
    """;
    private const string SummarySchema = """
    {"type":"object","additionalProperties":false,"required":["draftId","applicationId","stateSpaceId","opportunityProposalFingerprint","revision","candidateFingerprint","status","createdAtUtc","revisedAtUtc","expiresAtUtc","validationPassed","blockingFailures","scenarioFailures"],"properties":{"draftId":{"type":"string","minLength":1,"maxLength":80},"applicationId":{"type":"string","minLength":1,"maxLength":63},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"opportunityProposalFingerprint":{"type":"string","minLength":64,"maxLength":64},"revision":{"type":"integer","minimum":1,"maximum":8},"candidateFingerprint":{"type":"string","minLength":64,"maxLength":64},"status":{"enum":["draft","validated","approved-for-export","expired"]},"createdAtUtc":{"type":"string","minLength":1,"maxLength":64},"revisedAtUtc":{"type":"string","minLength":1,"maxLength":64},"expiresAtUtc":{"type":"string","minLength":1,"maxLength":64},"validationPassed":{"type":"boolean"},"blockingFailures":{"type":"integer","minimum":0},"scenarioFailures":{"type":"integer","minimum":0}}}
    """;
    private const string ValidationSchema = """
    {"type":"object","additionalProperties":false,"required":["passed","catalogChecks","antiSprawlChecks","scenarioResults","validatedAtUtc"],"properties":{"passed":{"type":"boolean"},"catalogChecks":{"type":"array","maxItems":64,"items":{"$ref":"#/$defs/check"}},"antiSprawlChecks":{"type":"array","maxItems":64,"items":{"$ref":"#/$defs/check"}},"scenarioResults":{"type":"array","maxItems":8,"items":{"type":"object","additionalProperties":false,"required":["name","passed","sandboxOk","effectCount","elapsedMilliseconds","limitHit","summary","effectPreviews"],"properties":{"name":{"type":"string","minLength":1,"maxLength":100},"passed":{"type":"boolean"},"sandboxOk":{"type":"boolean"},"effectCount":{"type":"integer","minimum":0,"maximum":50},"elapsedMilliseconds":{"type":"integer","minimum":0},"limitHit":{"type":"string","maxLength":100},"summary":{"type":"string","minLength":1,"maxLength":1000},"effectPreviews":{"type":"array","maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["type","entityId","definitionId","toEntityId","kind","slot","name","dataJson"],"properties":{"type":{"type":"string","minLength":1,"maxLength":100},"entityId":{"type":"string","maxLength":200},"definitionId":{"type":"string","maxLength":200},"toEntityId":{"type":"string","maxLength":200},"kind":{"type":"string","maxLength":200},"slot":{"type":"string","maxLength":200},"name":{"type":"string","maxLength":400},"dataJson":{"type":"string","minLength":2,"maxLength":65536}}}}}}},"validatedAtUtc":{"type":"string","minLength":1,"maxLength":64}}}
    """;
    private const string CheckSchema = """
    {"type":"object","additionalProperties":false,"required":["name","passed","blocking","summary"],"properties":{"name":{"type":"string","minLength":1,"maxLength":200},"passed":{"type":"boolean"},"blocking":{"type":"boolean"},"summary":{"type":"string","minLength":1,"maxLength":1000}}}
    """;
    private const string DetailSchema = """
    {"type":"object","additionalProperties":false,"required":["draft","candidate","validation"],"properties":{"draft":{"$ref":"#/$defs/summary"},"candidate":{"$ref":"#/$defs/candidate"},"validation":{"$ref":"#/$defs/validation"}}}
    """;

    public static readonly string DraftInput = """
    {"$defs":{"candidate":__CANDIDATE__},"type":"object","additionalProperties":false,"required":["applicationId","stateSpaceId","opportunityProposalFingerprint","candidate","idempotencyKey"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"opportunityProposalFingerprint":__HASH__,"candidate":{"$ref":"#/$defs/candidate"},"idempotencyKey":{"type":"string","minLength":1,"maxLength":128},"draftId":{"type":"string","minLength":1,"maxLength":80},"expectedRevision":{"type":"integer","minimum":1,"maximum":8}}}
    """.Replace("__CANDIDATE__", CandidateSchema, StringComparison.Ordinal).Replace("__HASH__", HashSchema, StringComparison.Ordinal);
    public static readonly string DraftOutput = """
    {"$defs":{"summary":__SUMMARY__,"candidate":__CANDIDATE__,"check":__CHECK__,"validation":__VALIDATION__},"type":"object","additionalProperties":false,"required":["outcome","draft","candidate","validation"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"draft":{"$ref":"#/$defs/summary"},"candidate":{"$ref":"#/$defs/candidate"},"validation":{"$ref":"#/$defs/validation"}}}
    """.Replace("__SUMMARY__", SummarySchema, StringComparison.Ordinal)
        .Replace("__CANDIDATE__", CandidateSchema, StringComparison.Ordinal)
        .Replace("__CHECK__", CheckSchema, StringComparison.Ordinal)
        .Replace("__VALIDATION__", ValidationSchema, StringComparison.Ordinal);
    public const string PromoteInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","stateSpaceId","draftId","expectedRevision","idempotencyKey"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"draftId":{"type":"string","minLength":1,"maxLength":80},"expectedRevision":{"type":"integer","minimum":1,"maximum":8},"idempotencyKey":{"type":"string","minLength":1,"maxLength":128}}}
    """;
    public static readonly string PromoteOutput = """
    {"$defs":{"summary":__SUMMARY__,"candidate":__CANDIDATE__,"check":__CHECK__,"validation":__VALIDATION__},"type":"object","additionalProperties":false,"required":["outcome","draft","export"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"draft":{"$ref":"#/$defs/summary"},"export":{"type":"object","additionalProperties":false,"required":["draftId","revision","candidateFingerprint","opportunityProposalFingerprint","permanentIdRequired","filesystemWritePerformed","activated","candidate","validation"],"properties":{"draftId":{"type":"string","minLength":1,"maxLength":80},"revision":{"type":"integer","minimum":1,"maximum":8},"candidateFingerprint":{"type":"string","minLength":64,"maxLength":64},"opportunityProposalFingerprint":{"type":"string","minLength":64,"maxLength":64},"permanentIdRequired":{"type":"boolean"},"filesystemWritePerformed":{"type":"boolean"},"activated":{"type":"boolean"},"candidate":{"$ref":"#/$defs/candidate"},"validation":{"$ref":"#/$defs/validation"}}}}}
    """.Replace("__SUMMARY__", SummarySchema, StringComparison.Ordinal)
        .Replace("__CANDIDATE__", CandidateSchema, StringComparison.Ordinal)
        .Replace("__CHECK__", CheckSchema, StringComparison.Ordinal)
        .Replace("__VALIDATION__", ValidationSchema, StringComparison.Ordinal);
    public const string ReadInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"draftId":{"type":"string","minLength":1,"maxLength":80},"limit":{"type":"integer","minimum":1,"maximum":50}}}
    """;
    public static readonly string ReadOutput = """
    {"$defs":{"summary":__SUMMARY__,"candidate":__CANDIDATE__,"check":__CHECK__,"validation":__VALIDATION__,"detail":__DETAIL__},"type":"object","additionalProperties":false,"required":["items","detail"],"properties":{"items":{"type":"array","maxItems":50,"items":{"$ref":"#/$defs/summary"}},"detail":{"anyOf":[{"type":"null"},{"$ref":"#/$defs/detail"}]}}}
    """.Replace("__SUMMARY__", SummarySchema, StringComparison.Ordinal)
        .Replace("__CANDIDATE__", CandidateSchema, StringComparison.Ordinal)
        .Replace("__CHECK__", CheckSchema, StringComparison.Ordinal)
        .Replace("__VALIDATION__", ValidationSchema, StringComparison.Ordinal)
        .Replace("__DETAIL__", DetailSchema, StringComparison.Ordinal);

    public static InteractionMechanicSandboxDraftCommand DraftCommand(JsonElement input) => new(
        ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!),
        input.GetProperty("stateSpaceId").GetString()!,
        input.GetProperty("opportunityProposalFingerprint").GetString()!,
        Candidate(input.GetProperty("candidate")),
        input.GetProperty("idempotencyKey").GetString()!,
        input.TryGetProperty("draftId", out var draftId) ? draftId.GetString() : null,
        input.TryGetProperty("expectedRevision", out var revision) ? revision.GetInt32() : null);

    public static InteractionMechanicSandboxPromotionCommand PromotionCommand(JsonElement input) => new(
        ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!),
        input.GetProperty("stateSpaceId").GetString()!, input.GetProperty("draftId").GetString()!,
        input.GetProperty("expectedRevision").GetInt32(), input.GetProperty("idempotencyKey").GetString()!);

    private static InteractionMechanicSandboxCandidate Candidate(JsonElement value)
    {
        var limits = value.GetProperty("limits");
        var allowlist = value.GetProperty("effectAllowlist");
        return new(value.GetProperty("name").GetString()!, value.GetProperty("category").GetString()!,
            value.GetProperty("description").GetString()!, Strings(value.GetProperty("matchPhrases")),
            value.GetProperty("requirements").GetRawText(), value.GetProperty("source").GetString()!,
            new(Strings(allowlist.GetProperty("effectTypes")), Strings(allowlist.GetProperty("componentIds"))),
            new(limits.GetProperty("maxStatements").GetInt32(), limits.GetProperty("timeoutMilliseconds").GetInt32(),
                limits.GetProperty("memoryBytes").GetInt32(), limits.GetProperty("maxRecursionDepth").GetInt32(),
                limits.GetProperty("maxEffects").GetInt32(), limits.GetProperty("maxEvents").GetInt32(),
                limits.GetProperty("maxNotifications").GetInt32(), limits.GetProperty("maxLogLines").GetInt32()),
            value.GetProperty("scenarios").EnumerateArray().Select(Scenario).ToArray());
    }

    private static InteractionMechanicSandboxScenario Scenario(JsonElement value)
    {
        var expected = value.GetProperty("expected");
        return new(value.GetProperty("name").GetString()!, value.GetProperty("projection").GetRawText(),
            new(expected.GetProperty("successful").GetBoolean(), expected.GetProperty("minimumEffects").GetInt32(),
                expected.GetProperty("maximumEffects").GetInt32(), Strings(expected.GetProperty("effectTypes")),
                Strings(expected.GetProperty("componentIds")),
                expected.TryGetProperty("narrationContains", out var narration) ? narration.GetString()! : ""));
    }

    private static IReadOnlyList<string> Strings(JsonElement value) =>
        value.EnumerateArray().Select(item => item.GetString()!).ToArray();

    public static JsonElement DraftData(InteractionMechanicSandboxDraftProjection value) => Element(new
    {
        outcome = value.Revision == 1 ? "created" : "revised",
        draft = Summary(value),
        candidate = CandidateData(value.Candidate),
        validation = value.Validation
    });

    public static JsonElement PromotionData(InteractionMechanicSandboxDraftProjection draft,
        InteractionMechanicSandboxExportPackage export) => Element(new
    {
        outcome = "approved-for-export",
        draft = Summary(draft),
        export = new
        {
            export.DraftId, export.Revision, export.CandidateFingerprint,
            export.OpportunityProposalFingerprint, export.PermanentIdRequired,
            export.FilesystemWritePerformed, export.Activated,
            candidate = CandidateData(export.Candidate),
            validation = export.Validation
        }
    });

    public static JsonElement ReadData(IReadOnlyList<InteractionMechanicSandboxDraftProjection> values,
        InteractionMechanicSandboxDraftProjection? detail = null) =>
        Element(new
        {
            items = values.Select(Summary).ToArray(),
            detail = detail is null ? null : new
            {
                draft = Summary(detail),
                candidate = CandidateData(detail.Candidate),
                validation = detail.Validation
            }
        });

    private static object CandidateData(InteractionMechanicSandboxCandidate value) => new
    {
        value.Name,
        value.Category,
        value.Description,
        value.MatchPhrases,
        requirements = JsonSerializer.Deserialize<JsonElement>(value.RequirementsJson),
        value.Source,
        value.EffectAllowlist,
        value.Limits,
        scenarios = value.Scenarios.Select(scenario => new
        {
            scenario.Name,
            projection = JsonSerializer.Deserialize<JsonElement>(scenario.ProjectionJson),
            scenario.Expected
        }).ToArray()
    };

    private static object Summary(InteractionMechanicSandboxDraftProjection value) => new
    {
        value.DraftId,
        applicationId = value.ApplicationId.Value,
        value.StateSpaceId,
        value.OpportunityProposalFingerprint,
        value.Revision,
        value.CandidateFingerprint,
        value.Status,
        value.CreatedAtUtc,
        value.RevisedAtUtc,
        value.ExpiresAtUtc,
        validationPassed = value.Validation.Passed,
        blockingFailures = value.Validation.CatalogChecks.Count(item => item.Blocking && !item.Passed)
            + value.Validation.AntiSprawlChecks.Count(item => item.Blocking && !item.Passed),
        scenarioFailures = value.Validation.ScenarioResults.Count(item => !item.Passed)
    };

    public static void RequireScope(SystemCapabilityInvocationContext context,
        ApplicationIdentifier applicationId, string stateSpaceId)
    {
        if (context.ApplicationId is not null && context.ApplicationId != applicationId
            || !string.IsNullOrEmpty(context.StateSpaceId) && context.StateSpaceId != stateSpaceId)
            throw new InteractionContractException("MECHANIC_SANDBOX_SCOPE_MISMATCH",
                "The trusted invocation scope does not match the sandbox request.");
    }

    public static string AuthorizationReference(AuthorizationAuditEvidence value) =>
        "mechanic-sandbox.authorization." + Hash(JsonSerializer.Serialize(value))[..32].ToLowerInvariant();

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "The sandbox request was rejected."
        : value.Length <= 300 ? value : value[..300];
    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
