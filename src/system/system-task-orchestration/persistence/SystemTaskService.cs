using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DantesRoleplay.Assistants;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Retrieval;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks;

public sealed partial class SystemTaskService : ISystemTaskService
{
    public const string TaskClass = "control.system.plan-task";
    public const int MaximumPlanningRounds = 3;
    public const int MaximumSteps = 12;
    public const int MaximumWrites = 8;
    public const int MaximumIntentLength = 8_000;
    public const int MaximumIdempotencyKeyLength = 100;
    public const int MaximumInputBytes = 96 * 1024;
    public const int MaximumAggregateInputBytes = 512 * 1024;
    public const int MaximumAggregateReadOutputBytes = 1024 * 1024;
    private const int MaximumGuidanceCharacters = 16_000;
    private const string SystemPrompt = """
        You are the bounded system-task planner for a private operator. You have no tools and cannot
        execute writes. Use only capabilities and exact schemas in SYSTEM CONTEXT. Return read steps
        with disposition continue when more evidence is needed. Return write steps only with
        disposition prepared. A completed disposition means the request needed only reads or no
        change. Never invent capability identifiers, paths, secrets, authority, application ECS
        actions, arbitrary tools, SQL, URLs, or results. Stored guidance is a non-authoritative hint:
        current descriptors and host validation always win. Evidence entries must be copied exactly
        from supplied references. Return only JSON matching the response schema; omit hidden reasoning.
        """;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim PrepareGate = new(1, 1);

    private readonly DantesRoleplayDbContext _db;
    private readonly IAssistantConversationStore _conversations;
    private readonly ISystemCapabilityCatalog _capabilities;
    private readonly ISystemTaskContextMaterializer _context;
    private readonly ILocalStructuredCompletionProvider _provider;
    private readonly IPrivateOperatorAuthorizationPolicy _authorization;
    private readonly IBoundedJsonSchemaValidator _schemas;

    public SystemTaskService(
        DantesRoleplayDbContext db,
        IAssistantConversationStore conversations,
        ISystemCapabilityCatalog capabilities,
        ISystemTaskContextMaterializer context,
        ILocalStructuredCompletionProvider provider,
        IPrivateOperatorAuthorizationPolicy authorization,
        IBoundedJsonSchemaValidator schemas)
    {
        _db = db;
        _conversations = conversations;
        _capabilities = capabilities;
        _context = context;
        _provider = provider;
        _authorization = authorization;
        _schemas = schemas;
    }

    public async Task<SystemTaskDocument> PrepareAsync(
        SystemTaskRequestContext context,
        string conversationId,
        SystemTaskPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        Authorize(context, PrivateOperatorCapability.ControlAiMessage);
        ValidateConversationId(conversationId);
        await RequireConversationAsync(context, conversationId, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        var operation = NormalizeOperation(request.Operation);
        var intent = NormalizeIntent(request.Intent);
        ValidateIdempotencyKey(request.IdempotencyKey);
        var agenda = NormalizeAgenda(operation, request.Agenda);
        var requestFingerprint = Hash(Canonical(new JsonObject
        {
            ["operation"] = operation,
            ["intent"] = intent,
            ["agenda"] = agenda is null ? null : JsonSerializer.SerializeToNode(agenda, WebJson)
        }));

        await PrepareGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.SystemTasks.AsNoTracking().SingleOrDefaultAsync(value =>
                value.PrincipalReference == context.Principal.PrincipalId &&
                value.ConversationId == conversationId &&
                value.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
                    throw Error("SYSTEM_TASK_IDEMPOTENCY_CONFLICT",
                        "The task idempotency key was already used for a different request.");
                if (!SystemTaskStatuses.IsTerminal(existing.Status))
                    await TerminalizeInterruptedAsync(existing.Id, cancellationToken);
                return await ExactAsync(context, existing.Id, cancellationToken);
            }

            var task = new SystemTaskRecord
            {
                Id = NewId("system-task."),
                PrincipalReference = context.Principal.PrincipalId,
                ConversationId = conversationId,
                Operation = operation,
                Intent = intent,
                IdempotencyKey = request.IdempotencyKey,
                RequestFingerprint = requestFingerprint,
                Status = SystemTaskStatuses.Planning,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.SystemTasks.Add(task);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var snapshot = await _context.MaterializeAsync(intent, context, cancellationToken);
                task.ContextProfile = snapshot.Profile;
                task.ContextFingerprint = snapshot.Fingerprint;
                task.ContextSourceReferencesJson = JsonSerializer.Serialize(snapshot.SourceReferences, WebJson);
                await _db.SaveChangesAsync(cancellationToken);

                var invocation = Invocation(context);
                var discovery = _capabilities.Discover(invocation);
                if (!discovery.Ok)
                    throw Error(discovery.Error?.Code ?? "SYSTEM_TASK_CAPABILITIES_UNAVAILABLE",
                        "Authorized system capabilities are unavailable.");
                var safeDescriptors = discovery.Capabilities
                    .Where(value => value.Sensitivity != SystemCapabilitySensitivity.Secret)
                    .ToDictionary(value => value.Id, StringComparer.Ordinal);
                var visibleReferences = snapshot.SourceReferences.ToHashSet(StringComparer.Ordinal);
                var descriptors = operation == SystemTaskOperations.Submit
                    ? safeDescriptors
                    : safeDescriptors.Where(value => visibleReferences.Contains(
                            $"capability:{value.Value.Id}@{value.Value.Version}#{value.Value.Fingerprint}"))
                        .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

                if (operation == SystemTaskOperations.Submit)
                    await PrepareAgendaAsync(task, agenda!, descriptors, invocation, cancellationToken);
                else
                    await ResolveAsync(task, snapshot, descriptors, invocation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await CompleteTaskAsync(task.Id, SystemTaskStatuses.Unavailable,
                    "Task planning was cancelled.", "SYSTEM_TASK_CANCELLED",
                    "The system task was cancelled before a plan was prepared.", CancellationToken.None);
            }
            catch (SystemTaskException exception)
            {
                await CompleteTaskAsync(task.Id, StatusFor(exception.Code),
                    "The task could not be prepared.", SafeCode(exception.Code),
                    SafeMessage(exception.Message), CancellationToken.None);
            }
            catch (Exception)
            {
                await CompleteTaskAsync(task.Id, SystemTaskStatuses.Failed,
                    "The task could not be prepared.", "SYSTEM_TASK_PREPARATION_FAILED",
                    "The system task failed safely before execution.", CancellationToken.None);
            }
            return await ExactAsync(context, task.Id, CancellationToken.None);
        }
        finally
        {
            PrepareGate.Release();
        }
    }

    public async Task<SystemTaskDocument?> GetAsync(
        SystemTaskRequestContext context,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        Authorize(context, PrivateOperatorCapability.ControlRead);
        ValidateTaskId(taskId);
        var task = await LoadAsync(taskId, cancellationToken);
        return task is null || !string.Equals(task.PrincipalReference, context.Principal.PrincipalId,
            StringComparison.Ordinal) ? null : Document(task);
    }

    public async Task<IReadOnlyList<SystemTaskSummary>> ListAsync(
        SystemTaskRequestContext context,
        string conversationId,
        DateTime? beforeCreatedAtUtc,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        Authorize(context, PrivateOperatorCapability.ControlRead);
        ValidateConversationId(conversationId);
        if (limit is < 1 or > 100) throw Error("SYSTEM_TASK_LIMIT_INVALID", "The task limit is invalid.");
        if (beforeCreatedAtUtc.HasValue) ValidateTaskId(beforeId ?? string.Empty);
        await RequireConversationAsync(context, conversationId, cancellationToken);
        var query = _db.SystemTasks.AsNoTracking().Where(value =>
            value.PrincipalReference == context.Principal.PrincipalId && value.ConversationId == conversationId);
        if (beforeCreatedAtUtc.HasValue)
        {
            var before = beforeCreatedAtUtc.Value;
            query = query.Where(value => value.CreatedAtUtc < before ||
                value.CreatedAtUtc == before && string.Compare(value.Id, beforeId) < 0);
        }
        var values = await query.OrderByDescending(value => value.CreatedAtUtc)
            .ThenByDescending(value => value.Id).Take(limit).ToListAsync(cancellationToken);
        return Array.AsReadOnly(values.Select(Summary).ToArray());
    }

    private async Task ResolveAsync(
        SystemTaskRecord task,
        SystemTaskContextSnapshot snapshot,
        IReadOnlyDictionary<string, SystemCapabilityDescriptor> descriptors,
        SystemCapabilityInvocationContext invocation,
        CancellationToken cancellationToken)
    {
        var responseSchema = ResponseSchema(descriptors.Values);
        var compiled = _schemas.Compile(responseSchema);
        if (!compiled.IsAccepted)
            throw Error("SYSTEM_TASK_RESPONSE_SCHEMA_INVALID", "The task planner response contract is unavailable.");
        var guidance = await GuidanceAsync(task.Intent, cancellationToken);

        for (var round = 1; round <= MaximumPlanningRounds; round++)
        {
            var prior = await _db.SystemTaskSteps.AsNoTracking().Where(value => value.TaskId == task.Id)
                .OrderBy(value => value.Ordinal).Select(value => new
                {
                    value.StepId, value.CapabilityId, value.Mode, value.InputJson,
                    value.ResultJson, value.ResultFingerprint
                }).ToListAsync(cancellationToken);
            StructuredCompletionResult completion;
            try
            {
                completion = await _provider.CompleteAsync(new(
                    TaskClass, SystemPrompt,
                    Prompt(task.Intent, snapshot.Json, guidance, prior),
                    compiled.NormalizedSchema, LocalModelPriority.Interactive), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw Error("SYSTEM_TASK_MODEL_UNAVAILABLE", "The local task planner is unavailable.");
            }
            if (!completion.Ok || completion.Identity is null)
                throw Error(SafeCode(completion.ErrorCode), SafeMessage(completion.ErrorMessage));

            var parsed = ParsePlannerResponse(compiled.NormalizedSchema, completion.Json,
                snapshot.SourceReferences, descriptors);
            _db.SystemTaskRounds.Add(new()
            {
                TaskId = task.Id,
                Ordinal = round,
                Disposition = parsed.Disposition,
                Summary = parsed.Summary,
                ContextFingerprint = snapshot.Fingerprint,
                ResponseFingerprint = Hash(Canonical(parsed.Raw)),
                ModelProvider = BoundedIdentity(completion.Identity.Provider, 50),
                Model = BoundedIdentity(completion.Identity.Model, 200),
                ModelRevision = BoundedIdentity(completion.Identity.Revision, 200),
                ModelProfile = BoundedIdentity(completion.Identity.Profile, 100),
                EvidenceJson = JsonSerializer.Serialize(parsed.Evidence, WebJson),
                OutputJson = Canonical(parsed.Raw),
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);

            if (parsed.Disposition == "continue")
            {
                if (parsed.Steps.Count == 0 || parsed.Steps.Any(value =>
                    !descriptors.TryGetValue(value.CapabilityId, out var descriptor) ||
                    descriptor.Mode != SystemCapabilityMode.Read))
                    throw Error("SYSTEM_TASK_MODEL_PLAN_INVALID", "A planning round proposed invalid read steps.");
                await AddStepsAsync(task, parsed.Steps, descriptors, invocation,
                    readsOnly: true, cancellationToken: cancellationToken);
                if (round == MaximumPlanningRounds)
                {
                    await CompleteTaskAsync(task.Id, SystemTaskStatuses.NeedsInput,
                        parsed.Summary, "SYSTEM_TASK_ROUND_LIMIT",
                        "The planner needs a clearer request after three bounded rounds.", cancellationToken);
                    return;
                }
                continue;
            }

            if (parsed.Disposition == "prepared")
            {
                if (parsed.Steps.Count == 0 || parsed.Steps.Any(value =>
                    !descriptors.TryGetValue(value.CapabilityId, out var descriptor) ||
                    descriptor.Mode != SystemCapabilityMode.Write))
                    throw Error("SYSTEM_TASK_MODEL_PLAN_INVALID", "The prepared plan contains invalid write steps.");
                await AddStepsAsync(task, parsed.Steps, descriptors, invocation,
                    writesOnly: true, cancellationToken: cancellationToken);
                await FinishPreparedAsync(task.Id, parsed.Summary, cancellationToken);
                return;
            }

            if (parsed.Steps.Count != 0)
                throw Error("SYSTEM_TASK_MODEL_PLAN_INVALID", "A terminal planning response cannot contain steps.");
            var status = parsed.Disposition switch
            {
                "completed" => SystemTaskStatuses.Completed,
                "needs-input" => SystemTaskStatuses.NeedsInput,
                "unknown" => SystemTaskStatuses.Unknown,
                "unsupported" => SystemTaskStatuses.Unsupported,
                "unavailable" => SystemTaskStatuses.Unavailable,
                _ => throw Error("SYSTEM_TASK_MODEL_PLAN_INVALID", "The task planner returned an invalid disposition.")
            };
            await CompleteTaskAsync(task.Id, status, parsed.Summary, "", "", cancellationToken);
            return;
        }
    }

    private async Task PrepareAgendaAsync(
        SystemTaskRecord task,
        IReadOnlyList<SystemTaskAgendaItem> agenda,
        IReadOnlyDictionary<string, SystemCapabilityDescriptor> descriptors,
        SystemCapabilityInvocationContext invocation,
        CancellationToken cancellationToken)
    {
        var steps = agenda.Select(value => new DraftStep(value.CapabilityId, value.Input.Clone())).ToArray();
        var seenWrite = false;
        foreach (var step in steps)
        {
            if (!descriptors.TryGetValue(step.CapabilityId, out var descriptor))
                throw Error("SYSTEM_TASK_CAPABILITY_UNKNOWN", "The submitted agenda contains an unknown capability.");
            if (descriptor.Mode == SystemCapabilityMode.Write) seenWrite = true;
            else if (seenWrite)
                throw Error("SYSTEM_TASK_AGENDA_ORDER_INVALID", "Submitted reads must appear before writes.");
        }
        await AddStepsAsync(task, steps, descriptors, invocation, cancellationToken: cancellationToken);
        if (steps.Any(value => descriptors[value.CapabilityId].Mode == SystemCapabilityMode.Write))
            await FinishPreparedAsync(task.Id, "The submitted system task is prepared for confirmation.", cancellationToken);
        else
            await CompleteTaskAsync(task.Id, SystemTaskStatuses.Completed,
                "The submitted read task completed.", "", "", cancellationToken);
    }

    private async Task AddStepsAsync(
        SystemTaskRecord task,
        IReadOnlyList<DraftStep> proposed,
        IReadOnlyDictionary<string, SystemCapabilityDescriptor> descriptors,
        SystemCapabilityInvocationContext invocation,
        bool readsOnly = false,
        bool writesOnly = false,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.SystemTaskSteps.AsNoTracking().Where(value => value.TaskId == task.Id)
            .OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
        if (current.Count + proposed.Count > MaximumSteps)
            throw Error("SYSTEM_TASK_STEP_LIMIT", "The task exceeds the twelve-step limit.");
        var writes = current.Count(value => value.Mode == "write");
        var aggregateInputBytes = current.Sum(value => Encoding.UTF8.GetByteCount(value.InputJson));
        var aggregateReadOutputBytes = current.Where(value => value.Mode == "read")
            .Sum(value => Encoding.UTF8.GetByteCount(value.ResultJson));
        var validated = new List<ValidatedDraft>(proposed.Count);
        foreach (var draft in proposed)
        {
            if (!descriptors.TryGetValue(draft.CapabilityId, out var descriptor))
                throw Error("SYSTEM_TASK_CAPABILITY_UNKNOWN", "The task contains an unknown capability.");
            if (readsOnly && descriptor.Mode != SystemCapabilityMode.Read ||
                writesOnly && descriptor.Mode != SystemCapabilityMode.Write)
                throw Error("SYSTEM_TASK_CAPABILITY_MODE_INVALID", "The task capability mode is invalid for this round.");
            if (descriptor.Mode == SystemCapabilityMode.Write && ++writes > MaximumWrites)
                throw Error("SYSTEM_TASK_WRITE_LIMIT", "The task exceeds the eight-write limit.");
            if (draft.Input.ValueKind != JsonValueKind.Object)
                throw Error("SYSTEM_TASK_INPUT_INVALID", "Every capability input must be a JSON object.");
            var input = Canonical(draft.Input);
            var inputBytes = Encoding.UTF8.GetByteCount(input);
            aggregateInputBytes += inputBytes;
            if (inputBytes > MaximumInputBytes || aggregateInputBytes > MaximumAggregateInputBytes)
                throw Error("SYSTEM_TASK_INPUT_LIMIT",
                    "The task capability inputs exceed the bounded task limits.");
            validated.Add(new(descriptor, input));
        }
        foreach (var draft in validated)
        {
            var descriptor = draft.Descriptor;
            var input = draft.InputJson;
            var ordinal = current.Count + 1;
            var stepId = $"step-{ordinal:000}";
            if (descriptor.Mode == SystemCapabilityMode.Read)
            {
                var result = await _capabilities.ReadAsync(descriptor.Id, input, invocation, cancellationToken);
                if (!result.Ok || result.Data is null)
                    throw Error(result.Error?.Code ?? "SYSTEM_TASK_READ_FAILED",
                        result.Error?.Message ?? "A system task read failed safely.");
                var output = Canonical(result.Data.Value);
                aggregateReadOutputBytes += Encoding.UTF8.GetByteCount(output);
                if (aggregateReadOutputBytes > MaximumAggregateReadOutputBytes)
                    throw Error("SYSTEM_TASK_READ_OUTPUT_LIMIT",
                        "The task read results exceed the bounded retained-output limit.");
                current.Add(new()
                {
                    TaskId = task.Id, Ordinal = ordinal, StepId = stepId,
                    CapabilityId = descriptor.Id, CapabilityVersion = descriptor.Version,
                    DescriptorFingerprint = descriptor.Fingerprint, Owner = descriptor.Owner, Mode = "read",
                    InputJson = input, InputFingerprint = Hash(input), PreflightStatus = "read",
                    PreconditionFingerprint = "", SafeSummary = BoundedSummary(descriptor.Description),
                    AffectedReferencesJson = JsonSerializer.Serialize(new[]
                        { $"capability:{descriptor.Id}@{descriptor.Version}#{descriptor.Fingerprint}" }, WebJson),
                    DeferredStepIdsJson = "[]", ResultJson = output, ResultFingerprint = Hash(output)
                });
            }
            else
            {
                var earlier = current.Select(value => new SystemCapabilityEarlierStep(
                    value.StepId, value.CapabilityId, value.InputJson)).ToArray();
                var result = await _capabilities.PreflightWriteAsync(descriptor.Id,
                    descriptor.Fingerprint, input, earlier, invocation, cancellationToken);
                if (!result.Ok || result.Preflight is null)
                    throw Error(result.Error?.Code ?? "SYSTEM_TASK_PREFLIGHT_FAILED",
                        result.Error?.Message ?? "A system task write could not be prepared.");
                current.Add(new()
                {
                    TaskId = task.Id, Ordinal = ordinal, StepId = stepId,
                    CapabilityId = descriptor.Id, CapabilityVersion = descriptor.Version,
                    DescriptorFingerprint = descriptor.Fingerprint, Owner = descriptor.Owner, Mode = "write",
                    InputJson = input, InputFingerprint = Hash(input),
                    PreflightStatus = result.Preflight.Status,
                    PreconditionFingerprint = result.Preflight.PreconditionFingerprint,
                    SafeSummary = BoundedSummary(result.Preflight.SafeSummary),
                    AffectedReferencesJson = JsonSerializer.Serialize(result.Preflight.AffectedReferences, WebJson),
                    DeferredStepIdsJson = JsonSerializer.Serialize(result.Preflight.DeferredStepIds, WebJson),
                    ResultJson = "", ResultFingerprint = ""
                });
            }
            _db.SystemTaskSteps.Add(current[^1]);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task FinishPreparedAsync(string taskId, string summary, CancellationToken cancellationToken)
    {
        var task = await _db.SystemTasks.SingleAsync(value => value.Id == taskId, cancellationToken);
        var steps = await _db.SystemTaskSteps.AsNoTracking().Where(value => value.TaskId == taskId)
            .OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
        if (steps.Count(value => value.Mode == "write") is < 1 or > MaximumWrites)
            throw Error("SYSTEM_TASK_PLAN_INVALID", "A prepared task must contain bounded write steps.");
        task.PlanFingerprint = PlanFingerprint(steps);
        task.Status = SystemTaskStatuses.Prepared;
        task.SafeSummary = BoundedSummary(summary);
        task.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteTaskAsync(string taskId, string status, string summary,
        string code, string message, CancellationToken cancellationToken)
    {
        var task = await _db.SystemTasks.SingleAsync(value => value.Id == taskId, cancellationToken);
        var steps = await _db.SystemTaskSteps.AsNoTracking().Where(value => value.TaskId == taskId)
            .OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
        task.Status = status;
        task.SafeSummary = BoundedSummary(summary);
        task.PlanFingerprint = steps.Count == 0 ? "" : PlanFingerprint(steps);
        task.ErrorCode = SafeCode(code, allowEmpty: true);
        task.ErrorMessage = SafeMessage(message, allowEmpty: true);
        task.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task TerminalizeInterruptedAsync(string taskId, CancellationToken cancellationToken) =>
        await CompleteTaskAsync(taskId, SystemTaskStatuses.Unavailable,
            "The earlier planning attempt was interrupted.", "SYSTEM_TASK_INTERRUPTED",
            "Retry with a new idempotency key.", cancellationToken);

    private async Task<string> GuidanceAsync(string intent, CancellationToken cancellationToken)
    {
        var tokens = Tokens(intent);
        if (tokens.Count == 0) return "[]";
        var candidates = await _db.SystemTasks.AsNoTracking().Include(value => value.Steps)
            .Include(value => value.Executions)
            .Where(value => value.Executions.Any(execution =>
                execution.Status == SystemTaskExecutionStatuses.Succeeded))
            .OrderByDescending(value => value.CompletedAtUtc).Take(50).ToListAsync(cancellationToken);
        var matched = candidates.Where(value => Tokens(value.Intent).Any(tokens.Contains))
            .Take(6).Select(value => new
            {
                value.Intent,
                value.SafeSummary,
                value.PlanFingerprint,
                receiptId = value.Executions.Where(execution =>
                        execution.Status == SystemTaskExecutionStatuses.Succeeded)
                    .OrderByDescending(execution => execution.CompletedAtUtc)
                    .Select(execution => execution.Id).FirstOrDefault(),
                steps = value.Steps.Where(step => step.Mode == "write").OrderBy(step => step.Ordinal)
                    .Select(step => new
                    {
                        step.CapabilityId,
                        input = JsonNode.Parse(step.InputJson),
                        step.DescriptorFingerprint
                    })
            }).ToArray();
        var json = JsonSerializer.Serialize(matched, WebJson);
        return json.Length <= MaximumGuidanceCharacters ? json : "[]";
    }

    private PlannerResponse ParsePlannerResponse(
        string schema,
        string json,
        IReadOnlyList<string> allowedReferences,
        IReadOnlyDictionary<string, SystemCapabilityDescriptor> descriptors)
    {
        if (_schemas.Validate(schema, json).Status != SchemaValueStatus.Valid)
            throw Error("SYSTEM_TASK_MODEL_RESPONSE_INVALID", "The local task planner returned invalid structured data.");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var disposition = root.GetProperty("disposition").GetString()!;
            var summary = root.GetProperty("summary").GetString()!;
            var evidence = root.GetProperty("evidence").EnumerateArray()
                .Select(value => value.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            var allowed = allowedReferences.ToHashSet(StringComparer.Ordinal);
            if (evidence.Any(value => !allowed.Contains(value)))
                throw Error("SYSTEM_TASK_MODEL_EVIDENCE_INVALID", "The planner cited evidence outside its supplied context.");
            var steps = root.GetProperty("steps").EnumerateArray().Select(value =>
            {
                var id = value.GetProperty("capabilityId").GetString()!;
                if (!descriptors.ContainsKey(id))
                    throw Error("SYSTEM_TASK_CAPABILITY_UNKNOWN", "The planner proposed an unregistered capability.");
                return new DraftStep(id, value.GetProperty("input").Clone());
            }).ToArray();
            if (disposition is "continue" or "prepared" && evidence.Length == 0)
                throw Error("SYSTEM_TASK_MODEL_EVIDENCE_INVALID",
                    "A proposed task plan must cite supplied current evidence.");
            return new(disposition, summary, Array.AsReadOnly(evidence), Array.AsReadOnly(steps), root.Clone());
        }
        catch (SystemTaskException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Error("SYSTEM_TASK_MODEL_RESPONSE_INVALID", "The local task planner returned invalid structured data.");
        }
    }

    private static string ResponseSchema(IEnumerable<SystemCapabilityDescriptor> descriptors)
    {
        var ids = descriptors.Select(value => value.Id).OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray();
        var root = new JsonObject
        {
            ["$schema"] = SystemJsonSchemaProfile.MetaSchemaUri,
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("disposition", "summary", "evidence", "steps"),
            ["properties"] = new JsonObject
            {
                ["disposition"] = new JsonObject { ["enum"] = new JsonArray("continue", "prepared", "completed", "needs-input", "unknown", "unsupported", "unavailable") },
                ["summary"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1000 },
                ["evidence"] = new JsonObject { ["type"] = "array", ["maxItems"] = 24, ["uniqueItems"] = true,
                    ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 320 } },
                ["steps"] = new JsonObject { ["type"] = "array", ["maxItems"] = MaximumSteps,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object", ["additionalProperties"] = false,
                        ["required"] = new JsonArray("capabilityId", "input"),
                        ["properties"] = new JsonObject
                        {
                            ["capabilityId"] = new JsonObject { ["enum"] = new JsonArray(ids) },
                            ["input"] = new JsonObject { ["type"] = "object", ["maxProperties"] = 64 }
                        }
                    }}
            }
        };
        return root.ToJsonString(WebJson);
    }

    private static string Prompt(string intent, string contextJson, string guidance, object prior) =>
        "OPERATOR INTENT\n" + intent + "\n\nSYSTEM CONTEXT\n" + contextJson +
        "\n\nBOUNDED PRIOR SUCCESS HINTS\n" + guidance +
        "\n\nREAD RESULTS FROM THIS TASK\n" + JsonSerializer.Serialize(prior, WebJson);

    private async Task RequireConversationAsync(SystemTaskRequestContext context, string conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversations.GetAsync(context.Principal.PrincipalId, conversationId,
            cancellationToken, AssistantConversationScopes.System);
        if (conversation is null)
            throw Error("ASSISTANT_CONVERSATION_UNKNOWN", "The system conversation was not found.");
    }

    private async Task<SystemTaskDocument> ExactAsync(SystemTaskRequestContext context, string taskId,
        CancellationToken cancellationToken)
    {
        var task = await LoadAsync(taskId, cancellationToken);
        if (task is null || !string.Equals(task.PrincipalReference, context.Principal.PrincipalId, StringComparison.Ordinal))
            throw Error("SYSTEM_TASK_UNKNOWN", "The system task was not found.");
        return Document(task);
    }

    private Task<SystemTaskRecord?> LoadAsync(string taskId, CancellationToken cancellationToken) =>
        _db.SystemTasks.AsNoTracking()
            .Include(value => value.Rounds)
            .Include(value => value.Steps)
            .Include(value => value.Confirmations)
            .Include(value => value.Executions).ThenInclude(value => value.Steps)
            .SingleOrDefaultAsync(value => value.Id == taskId, cancellationToken);

    private static SystemTaskDocument Document(SystemTaskRecord task) => new(
        Summary(task), task.ContextProfile, task.ContextFingerprint,
        Strings(task.ContextSourceReferencesJson), task.ErrorCode, task.ErrorMessage,
        task.Rounds.OrderBy(value => value.Ordinal).Select(value => new SystemTaskRoundDocument(
            value.Ordinal, value.Disposition, value.Summary, value.ContextFingerprint,
            value.ResponseFingerprint, value.ModelProvider, value.Model, value.ModelRevision,
            value.ModelProfile, Strings(value.EvidenceJson), value.CreatedAtUtc)).ToArray(),
        task.Steps.OrderBy(value => value.Ordinal).Select(value => new SystemTaskStepDocument(
            value.StepId, value.Ordinal, value.CapabilityId, value.CapabilityVersion,
            value.DescriptorFingerprint, value.Owner, value.Mode, Element(value.InputJson)!.Value,
            value.InputFingerprint, value.PreflightStatus, value.PreconditionFingerprint,
            value.SafeSummary, Strings(value.AffectedReferencesJson), Strings(value.DeferredStepIdsJson),
            Element(value.ResultJson), value.ResultFingerprint)).ToArray(),
        task.Confirmations.OrderByDescending(value => value.ConfirmedAtUtc).Select(value =>
            new SystemTaskConfirmationDocument(value.Id, value.PlanFingerprint,
                value.ConfirmedAtUtc, value.ExpiresAtUtc)).ToArray(),
        task.Executions.OrderByDescending(value => value.CreatedAtUtc).Select(value => ExecutionDocument(value)).ToArray());

    private static SystemTaskSummary Summary(SystemTaskRecord value) => new(
        value.Id, value.ConversationId, value.Operation, value.Intent, value.Status,
        value.SafeSummary, value.PlanFingerprint, value.CreatedAtUtc, value.CompletedAtUtc);

    private static SystemTaskExecutionDocument ExecutionDocument(SystemTaskExecutionRecord value) => new(
        value.Id, value.ConfirmationId, value.Status, value.PlanFingerprint, value.SafeSummary,
        value.ErrorCode, value.ErrorMessage, value.CreatedAtUtc, value.StartedAtUtc, value.CompletedAtUtc,
        value.Steps.OrderBy(step => step.Ordinal).Select(step => new SystemTaskExecutionStepDocument(
            step.TaskStepId, step.Ordinal, step.Status, step.OperationId, Element(step.OutputJson),
            step.OutputFingerprint, step.ReadBackFingerprint, step.ErrorCode, step.ErrorMessage,
            step.CompletedAtUtc)).ToArray());

    private PrivateOperatorAuthorizationDecision Authorize(SystemTaskRequestContext context,
        PrivateOperatorCapability capability)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = _authorization.Evaluate(new(context.Principal, capability, context.Scope, context.CorrelationId));
        if (!decision.Allowed)
            throw Error(decision.Code, "Private-operator authorization is required for system tasks.");
        return decision;
    }

    private static SystemCapabilityInvocationContext Invocation(SystemTaskRequestContext context) =>
        new(context.Principal, context.Scope, context.CorrelationId);

    private static IReadOnlyList<SystemTaskAgendaItem>? NormalizeAgenda(string operation,
        IReadOnlyList<SystemTaskAgendaItem>? agenda)
    {
        if (operation == SystemTaskOperations.Resolve)
        {
            if (agenda is { Count: > 0 }) throw Error("SYSTEM_TASK_AGENDA_INVALID", "Resolve requests cannot supply an agenda.");
            return null;
        }
        if (agenda is null || agenda.Count is < 1 or > MaximumSteps)
            throw Error("SYSTEM_TASK_AGENDA_INVALID", "Submit requests require one to twelve agenda items.");
        return Array.AsReadOnly(agenda.Select(value =>
        {
            if (!ValidCapabilityId(value.CapabilityId) || value.Input.ValueKind != JsonValueKind.Object)
                throw Error("SYSTEM_TASK_AGENDA_INVALID", "Every agenda item must contain a system capability and object input.");
            return new SystemTaskAgendaItem(value.CapabilityId, value.Input.Clone());
        }).ToArray());
    }

    private static string NormalizeOperation(string value) => SystemTaskOperations.IsKnown(value)
        ? value : throw Error("SYSTEM_TASK_OPERATION_INVALID", "The task operation must be resolve or submit.");

    private static string NormalizeIntent(string value)
    {
        if (value is null) throw Error("SYSTEM_TASK_INTENT_INVALID", "A task intent is required.");
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length is 0 or > MaximumIntentLength || normalized.Any(value => value == '\0'))
            throw Error("SYSTEM_TASK_INTENT_INVALID", "The task intent must contain one to 8000 characters.");
        return normalized;
    }

    private static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumIdempotencyKeyLength ||
            !IdempotencyPattern().IsMatch(value))
            throw Error("SYSTEM_TASK_IDEMPOTENCY_KEY_INVALID", "The task idempotency key is invalid.");
    }

    private static void ValidateConversationId(string value)
    {
        if (value?.Length != 45 || !value.StartsWith("conversation.", StringComparison.Ordinal) ||
            value[13..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Error("ASSISTANT_CONVERSATION_ID_INVALID", "The system conversation ID is invalid.");
    }

    private static void ValidateTaskId(string value)
    {
        if (value?.Length != 44 || !value.StartsWith("system-task.", StringComparison.Ordinal) ||
            value[12..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Error("SYSTEM_TASK_ID_INVALID", "The system task ID is invalid.");
    }

    private static string PlanFingerprint(IEnumerable<SystemTaskStepRecord> steps) => Hash(Canonical(
        JsonSerializer.SerializeToNode(steps.OrderBy(value => value.Ordinal).Select(value => new
        {
            value.Ordinal, value.StepId, value.CapabilityId, value.CapabilityVersion,
            value.DescriptorFingerprint, value.Owner, value.Mode, input = JsonNode.Parse(value.InputJson),
            value.InputFingerprint, value.PreflightStatus, value.PreconditionFingerprint,
            affectedReferences = JsonNode.Parse(value.AffectedReferencesJson),
            deferredStepIds = JsonNode.Parse(value.DeferredStepIdsJson)
        }), WebJson)!));

    private static string Canonical(JsonElement value) => Canonical(JsonNode.Parse(value.GetRawText())!);

    private static string Canonical(JsonNode value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is null) { writer.WriteNullValue(); return; }
        if (node is JsonObject obj)
        {
            writer.WriteStartObject();
            foreach (var property in obj.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Key); WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject(); return;
        }
        if (node is JsonArray array)
        {
            writer.WriteStartArray(); foreach (var item in array) WriteCanonical(writer, item);
            writer.WriteEndArray(); return;
        }
        node.WriteTo(writer);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N");
    private static string BoundedSummary(string value) =>
        string.IsNullOrWhiteSpace(value) ? "System task step." : value.Trim()[..Math.Min(value.Trim().Length, 1000)];
    private static string BoundedIdentity(string value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim()[..Math.Min(value.Trim().Length, maximum)];
    private static string SafeCode(string? value, bool allowEmpty = false) =>
        allowEmpty && string.IsNullOrEmpty(value) ? "" : value is { Length: >= 3 and <= 100 } &&
        value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_')
            ? value : "SYSTEM_TASK_UNAVAILABLE";
    private static string SafeMessage(string? value, bool allowEmpty = false) =>
        allowEmpty && string.IsNullOrEmpty(value) ? "" : string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(char.IsControl)
            ? "The system task is unavailable." : value;
    private static string StatusFor(string code) => code.Contains("UNAVAILABLE", StringComparison.Ordinal) ||
        code.Contains("CANCEL", StringComparison.Ordinal) ? SystemTaskStatuses.Unavailable :
        code.Contains("UNKNOWN", StringComparison.Ordinal) ? SystemTaskStatuses.Unknown :
        code.Contains("UNSUPPORTED", StringComparison.Ordinal) ? SystemTaskStatuses.Unsupported :
        code.Contains("INPUT", StringComparison.Ordinal) || code.Contains("INVALID", StringComparison.Ordinal) ||
        code.Contains("LIMIT", StringComparison.Ordinal) ? SystemTaskStatuses.NeedsInput : SystemTaskStatuses.Failed;
    private static bool ValidCapabilityId(string? value) => value is { Length: >= 8 and <= 120 } &&
        value.StartsWith("system.", StringComparison.Ordinal) && value.All(character =>
            char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-');
    private static HashSet<string> Tokens(string value) => value.Split(
            [' ', ',', ';', ':', '/', '\\', '(', ')', '"', '\'', '.', '?', '!'],
            StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Trim().ToLowerInvariant()).Where(value => value.Length >= 3)
        .Take(64).ToHashSet(StringComparer.Ordinal);
    private static IReadOnlyList<string> Strings(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json, WebJson) ?? []; }
        catch (JsonException) { return []; }
    }
    private static JsonElement? Element(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
    private static SystemTaskException Error(string code, string message) => new(code, message);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyPattern();

    private sealed record DraftStep(string CapabilityId, JsonElement Input);
    private sealed record ValidatedDraft(SystemCapabilityDescriptor Descriptor, string InputJson);
    private sealed record PlannerResponse(string Disposition, string Summary,
        IReadOnlyList<string> Evidence, IReadOnlyList<DraftStep> Steps, JsonElement Raw);
}
