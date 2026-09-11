using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.Procedures;
using DantesRoleplay.SystemTasks;
using Json.Schema;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Host-only inputs for producing a focused worker request.  This is intentionally internal until
/// the durable worker owner supplies the submission boundary.
/// </summary>
internal sealed record SystemInnerWorkerPreparationInput(
    SystemInnerWorkerRequest Worker,
    AiAgentProfile HostProfile,
    AiRequest HostConfiguration,
    AuthorizedInteractionEnvelope ContextEnvelope,
    InteractionAuthorizationRequest ContextAuthorization,
    IReadOnlyList<string> RequiredContextReferences,
    IReadOnlyList<string> PermittedToolNames,
    string SelectedResultSchemaJson);

/// <summary>Immutable evidence and provider inputs produced without starting a worker.</summary>
internal sealed record SystemInnerWorkerPreparedRequest(
    AiAgentProfile Profile,
    AiRequest Request,
    string ProcedureFingerprint,
    string ContextFingerprint,
    string OutputSchemaFingerprint,
    IReadOnlyList<string> SelectedContextReferences,
    int PromptBytes);

/// <summary>
/// Converts an exact, host-selected procedure and freshly authorized task-context pack into a
/// narrow AI request. It has no provider, tool, task, or budget-consumption dependency. The
/// carried grant is evidence for the later invocation boundary; this pure preparation does not
/// authorize a grant or make an approval decision.
/// </summary>
internal sealed class SystemInnerWorkerPreparation(
    IProcedureStore procedures,
    IInteractionTaskContextMaterializer contextMaterializer)
{
    private const int MaximumToolNames = 16;
    private const int MaximumContextReferences = InteractionTaskContextMaterializer.MaximumPackItems;
    private const int MaximumContextReferenceLength = 1_024;
    private const int MaximumPromptBytes = InteractionContractLimits.JsonBytes;

    public async Task<SystemInnerWorkerPreparedRequest> PrepareAsync(
        SystemInnerWorkerPreparationInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Worker);
        ArgumentNullException.ThrowIfNull(input.HostProfile);
        ArgumentNullException.ThrowIfNull(input.HostConfiguration);
        ArgumentNullException.ThrowIfNull(input.ContextEnvelope);
        ArgumentNullException.ThrowIfNull(input.ContextAuthorization);
        if (input.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow workflow)
            throw Failure("WORKER_PREPARATION_SUBJECT_UNSUPPORTED", "This preparation path requires an exact procedure workflow subject.");
        var selectedProcedure = workflow.ProcedureVersion;
        EnsureViableHost(input.Worker.InvocationHost, input.ContextEnvelope, input.ContextAuthorization);

        var procedure = await procedures.GetAsync(selectedProcedure.ExactDefinitionId,
            selectedProcedure.Version, cancellationToken);
        EnsureCurrentProcedure(procedure, selectedProcedure);

        var resultSchema = CanonicalSchema(input.SelectedResultSchemaJson, "WORKER_RESULT_SCHEMA_INVALID");
        if (!StringComparer.Ordinal.Equals(resultSchema, input.Worker.ResultSchemaJson))
            throw Failure("WORKER_RESULT_SCHEMA_MISMATCH", "The worker result schema differs from the host-selected schema.");

        var pack = await contextMaterializer.MaterializeAsync(input.ContextEnvelope, input.ContextAuthorization, cancellationToken);
        EnsureViableHost(input.Worker.InvocationHost, input.ContextEnvelope, input.ContextAuthorization);
        var currentProcedure = await procedures.GetAsync(selectedProcedure.ExactDefinitionId,
            selectedProcedure.Version, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureViableHost(input.Worker.InvocationHost, input.ContextEnvelope, input.ContextAuthorization);
        EnsureCurrentProcedure(currentProcedure, selectedProcedure);
        var selected = SelectContext(pack, input.RequiredContextReferences);
        var prompt = BuildPrompt(input.Worker.InputJson, selected);
        var promptBytes = Encoding.UTF8.GetByteCount(prompt);
        if (promptBytes > MaximumPromptBytes)
            throw Failure("WORKER_PROMPT_BUDGET_EXCEEDED", "The focused worker prompt exceeds its closed byte budget.");

        var tools = NormalizeNames(input.PermittedToolNames, "INVALID_WORKER_TOOL_ALLOWLIST");
        var request = BuildRequest(input.HostConfiguration, prompt, resultSchema, tools);
        var instructions = string.Join("\n\n", new[] { currentProcedure!.Instructions, currentProcedure.Constraints }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var profile = input.HostProfile with { Instructions = instructions };
        ValidateProfile(profile);
        return new(profile, request, currentProcedure.SourceHash, pack.Fingerprint,
            Sha256(resultSchema), Array.AsReadOnly(selected.Select(value => value.Reference).ToArray()), promptBytes);
    }

    private static void EnsureViableHost(InteractionInvocationHost worker, AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationRequest authorization)
    {
        if (worker.StateSpaceId is null || worker.StateRevision is null)
            throw Failure("WORKER_STATE_SCOPE_REQUIRED", "Procedure preparation requires an actual state scope.");
        if (worker.Profile == InteractionExecutionProfile.Atomic)
            throw Failure("ATOMIC_WORKER_PREPARATION_FORBIDDEN", "Atomic work cannot prepare a background worker.");
        if (worker.Budget.DeadlineUtc <= DateTime.UtcNow)
            throw Failure("WORKER_PREPARATION_DEADLINE_EXPIRED", "The worker deadline has expired.");
        if (worker.Budget.RemainingOperations < 1)
            throw Failure("WORKER_PREPARATION_BUDGET_EXHAUSTED", "The shared worker budget is exhausted.");
        if (worker.Principal.PrincipalId != envelope.Host.Principal.PrincipalId
            || worker.ApplicationRevision.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId
            || worker.ApplicationRevision.Revision != envelope.Host.ApplicationRevision.Revision
            || worker.ApplicationRevision.Fingerprint != envelope.Host.ApplicationRevision.Fingerprint
            || worker.StateSpaceId != envelope.Host.StateSpaceId
            || worker.StateRevision != envelope.Host.StateRevision
            || authorization.Principal.PrincipalId != worker.Principal.PrincipalId
            || authorization.ApplicationId != worker.ApplicationRevision.ApplicationId
            || authorization.StateSpaceId != worker.StateSpaceId
            || authorization.Capability != InteractionCapability.Plan)
            throw Failure("WORKER_PREPARATION_SCOPE_MISMATCH", "The worker and context authorization scopes do not match.");
    }

    private static void EnsureCurrentProcedure(ProcedureDetail? procedure, SystemTaskSelectedDefinition selected)
    {
        if (procedure is null)
            throw Failure("WORKER_PROCEDURE_MISSING", "The selected worker procedure is unavailable.");
        if (procedure.Status != ProcedureStatus.Active)
            throw Failure("WORKER_PROCEDURE_INACTIVE", "The selected worker procedure is not active.");
        if (procedure.Id != selected.ExactDefinitionId || procedure.Version != selected.Version
            || procedure.LatestVersion != selected.Version || procedure.SourceHash != selected.Fingerprint)
            throw Failure("WORKER_PROCEDURE_STALE", "The selected worker procedure is no longer the current exact revision.");
    }

    private static AiRequest BuildRequest(AiRequest configuration, string prompt, string schema, IReadOnlyList<string> tools)
    {
        if (string.IsNullOrWhiteSpace(configuration.Provider) || string.IsNullOrWhiteSpace(configuration.Model)
            || !Enum.IsDefined(configuration.Reasoning)
            || configuration.MaximumToolRounds < 0 || configuration.MaximumOutputTokens < 1
            || configuration.MaximumToolCalls is < 0 or > 16
            || configuration.MaximumResponseBytes is < 1 or > 1_048_576
            || configuration.MaximumDuration is { } duration && (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)))
            throw Failure("WORKER_HOST_CONFIGURATION_INVALID", "The host AI configuration is invalid.");
        return configuration with
        {
            Messages = [new AiMessage(AiMessageRole.User, prompt)], Kind = AiRequestKind.Task,
            ResponseSchemaJson = schema, AllowedTools = tools,
            MaximumToolRounds = Math.Min(configuration.MaximumToolRounds, 16),
            MaximumOutputTokens = Math.Min(configuration.MaximumOutputTokens, 131_072)
        };
    }

    private static string BuildPrompt(string inputJson, IReadOnlyList<SelectedContextItem> context)
    {
        var prompt = JsonSerializer.Serialize(new
        {
            input = JsonSerializer.Deserialize<JsonElement>(inputJson),
            context = context.Select(value => new { value.Reference, value.Revision, value.Fingerprint, value.Value })
        });
        if (Encoding.UTF8.GetByteCount(prompt) > MaximumPromptBytes)
            throw Failure("WORKER_PROMPT_BUDGET_EXCEEDED", "The focused worker prompt exceeds its closed byte budget.");
        return InteractionCanonicalJson.CanonicalizeObject(prompt);
    }

    private static IReadOnlyList<SelectedContextItem> SelectContext(InteractionTaskContextPack pack,
        IReadOnlyList<string> requiredReferences)
    {
        ArgumentNullException.ThrowIfNull(pack);
        if (pack.Profile != InteractionTaskContextProfiles.Version2 || string.IsNullOrWhiteSpace(pack.Json)
            || Encoding.UTF8.GetByteCount(pack.Json) > InteractionTaskContextMaterializer.MaximumPackBytes
            || !StringComparer.Ordinal.Equals(pack.Json, InteractionCanonicalJson.CanonicalizeObject(pack.Json))
            || !StringComparer.Ordinal.Equals(pack.Fingerprint, Sha256(pack.Json)))
            throw Failure("WORKER_CONTEXT_PACK_INVALID", "The materialized task-context pack is malformed or stale.");

        var requested = NormalizeContextReferences(requiredReferences, "INVALID_WORKER_CONTEXT_REFERENCES");
        using var document = JsonDocument.Parse(pack.Json);
        var actual = new Dictionary<string, SelectedContextItem>(StringComparer.Ordinal);
        foreach (var section in new[] { "scope", "capabilities", "readViews", "knowledge", "facts", "continuity", "recentReceipts" })
        {
            if (!document.RootElement.TryGetProperty(section, out var values) || values.ValueKind != JsonValueKind.Array)
                throw Failure("WORKER_CONTEXT_PACK_INVALID", "The task-context pack has an invalid section.");
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty("reference", out var reference) || reference.ValueKind != JsonValueKind.String
                    || !value.TryGetProperty("revision", out var revision) || revision.ValueKind != JsonValueKind.String
                    || !value.TryGetProperty("fingerprint", out var fingerprint) || fingerprint.ValueKind != JsonValueKind.String
                    || !value.TryGetProperty("value", out var payload))
                    throw Failure("WORKER_CONTEXT_PACK_INVALID", "A task-context item is malformed.");
                var key = reference.GetString()!;
                if (!actual.TryAdd(key, new(key, revision.GetString()!, fingerprint.GetString()!, payload.Clone())))
                    throw Failure("WORKER_CONTEXT_PACK_INVALID", "The task-context pack contains duplicate references.");
            }
        }
        var declared = NormalizeContextReferences(pack.SourceReferences, "WORKER_CONTEXT_PACK_INVALID");
        if (!actual.Keys.Order(StringComparer.Ordinal).SequenceEqual(declared, StringComparer.Ordinal))
            throw Failure("WORKER_CONTEXT_PACK_INVALID", "The task-context reference evidence does not match the pack.");
        var selected = new List<SelectedContextItem>();
        foreach (var reference in requested)
        {
            if (!actual.TryGetValue(reference, out var item))
                throw Failure("WORKER_REQUIRED_CONTEXT_MISSING", $"Required task context '{reference}' is unavailable.");
            selected.Add(item);
        }
        return selected;
    }

    private static IReadOnlyList<string> NormalizeNames(IReadOnlyList<string> values, string code)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > MaximumToolNames || values.Any(value => !ToolName.IsMatch(value))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw Failure(code, "The focused worker selection is invalid or exceeds its bound.");
        return values.Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> NormalizeContextReferences(IReadOnlyList<string> values, string code)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > MaximumContextReferences
            || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > MaximumContextReferenceLength)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw Failure(code, "The focused worker context references are invalid or exceed their bound.");
        return values.Order(StringComparer.Ordinal).ToArray();
    }

    private static string CanonicalSchema(string schema, string code)
    {
        try
        {
            var canonical = InteractionCanonicalJson.CanonicalizeObject(schema);
            _ = JsonSchema.FromText(canonical, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            return canonical;
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException or ArgumentException or InteractionContractException)
        {
            throw Failure(code, "The selected worker result schema does not compile.");
        }
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateProfile(AiAgentProfile profile)
    {
        if (!AgentId.IsMatch(profile.Id)
            || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 120
            || string.IsNullOrWhiteSpace(profile.Identity) || profile.Identity.Length > 2_000
            || profile.Instructions is null || profile.Instructions.Length > 8_000)
            throw Failure("WORKER_PROFILE_INVALID", "The host AI profile is invalid for focused worker preparation.");
    }

    private static readonly Regex ToolName = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex AgentId = new("^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant);
    private static InteractionContractException Failure(string code, string message) => new(code, message);
    private sealed record SelectedContextItem(string Reference, string Revision, string Fingerprint, JsonElement Value);
}
