using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Content;
using DantesRoleplay.Sources;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed record SystemInnerWorkerProcedurePreparationResult(
    SystemInnerWorkerRequest Worker,
    SystemInnerWorkerPreparedRequest Prepared,
    SystemTaskSelectedDefinition ProfileVersion,
    IReadOnlyList<SystemInnerWorkerToolBinding> ToolBindings,
    SystemInnerWorkerManualContextEvidence ContextEvidence,
    SystemInnerWorkerAiBudget AiBudget,
    SystemCapabilityInvocationContext ToolContext)
{
    internal SystemInnerWorkerResolvedProfile BindAuthority(SystemInnerWorkerAuthorityProvenance authority) => new(
        Worker, ProfileVersion, Prepared.Profile, Prepared.OutputSchemaFingerprint, ToolBindings,
        Prepared.SelectedContextReferences, ContextEvidence, authority, AiBudget);
}

/// <summary>
/// The first procedure-worker assignment grammar. It carries only bounded work text. Procedure,
/// context, tools, model, schema, authority and budgets are resolved independently by the host.
/// </summary>
internal sealed record SystemInnerWorkerAssignmentV1(string Instruction)
{
    internal const string Format = "dantes-roleplay/inner-procedure-assignment/v1";
    private static readonly HashSet<string> Properties = new(StringComparer.Ordinal) { "format", "instruction" };

    internal static SystemInnerWorkerAssignmentV1 Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(InteractionCanonicalJson.CanonicalizeObject(json));
            var root = document.RootElement;
            if (root.EnumerateObject().Any(value => !Properties.Contains(value.Name))
                || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String
                || format.GetString() != Format
                || !root.TryGetProperty("instruction", out var instruction) || instruction.ValueKind != JsonValueKind.String)
                throw Failure("INNER_WORKER_ASSIGNMENT_UNSUPPORTED",
                    "The procedure worker assignment does not use the supported version-1 grammar.");
            var value = instruction.GetString()!;
            if (string.IsNullOrWhiteSpace(value) || value.Length > InteractionContractLimits.IntentText)
                throw Failure("INNER_WORKER_ASSIGNMENT_INVALID", "The procedure worker instruction is missing or exceeds its bound.");
            return new(value);
        }
        catch (InteractionContractException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw Failure("INNER_WORKER_ASSIGNMENT_INVALID", "The procedure worker assignment is not a valid object.");
        }
    }

    private static InteractionContractException Failure(string code, string message) => new(code, message);
}

/// <summary>
/// Resolves the one existing INNER profile and the capabilities explicitly governed by the exact
/// selected procedure. This is a policy adapter over existing registries, not another registry.
/// </summary>
internal sealed class SystemInnerWorkerHostPolicy(
    IAiAgentProfileRegistry? profiles = null,
    ISystemCapabilityCatalog? capabilities = null)
{
    private const string InnerProfileId = "web.inner";

    internal SystemInnerWorkerHostSelection Resolve(SystemInnerWorkerRequest worker,
        SystemCapabilityInvocationContext context, DateTime nowUtc)
    {
        var profile = profiles?.Get(InnerProfileId)
            ?? throw Failure("INNER_WORKER_PROFILE_UNAVAILABLE", "The host has no focused INNER profile configured.");
        var procedure = worker.ProcedureVersion
            ?? throw Failure("INNER_WORKER_SUBJECT_UNSUPPORTED", "Only exact procedure workflow subjects use this runtime.");
        var governedProcedureId = "procedure." + procedure.ExactDefinitionId;
        var descriptors = capabilities?.Discover(context) is { Ok: true } discovered
            ? discovered.Capabilities
                .Where(value => value.ProcedureIds.Contains(governedProcedureId, StringComparer.Ordinal))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToArray()
            : [];
        var bindings = descriptors.Select(value => new SystemInnerWorkerToolBinding(
            new(ToolName(value.Id),
                value.Mode == SystemCapabilityMode.Read
                    ? $"Read the in-process system capability '{value.Contract.Id}'. {value.Contract.Description}"
                    : $"Write through the in-process system capability '{value.Contract.Id}'. Trusted confirmation and an idempotency token are required. {value.Contract.Description}",
                value.Contract.Input.SchemaJson),
            new(value.Id, value.Version, value.Fingerprint), value.Mode)).ToArray();
        var remaining = worker.InvocationHost.Budget.DeadlineUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
            throw Failure("INNER_WORKER_DEADLINE_EXPIRED", "The focused worker deadline has expired.");
        var budget = new SystemInnerWorkerAiBudget(toolCalls: Math.Min(bindings.Length == 0 ? 0 : 8,
            SystemInnerWorkerAiBudget.MaximumToolCalls));
        var configuration = new AiRequest(
            "codex", InteractionRoleProfile.Inner.Model, [], AiRequestKind.Task, AiReasoningEffort.Low,
            MaximumToolRounds: bindings.Length == 0 ? 0 : 4,
            MaximumOutputTokens: 2_048,
            MaximumToolCalls: budget.ToolCalls,
            MaximumResponseBytes: InteractionContractLimits.JsonBytes,
            MaximumDuration: remaining > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : remaining);
        return new(profile, configuration, bindings, budget);
    }

    private static string ToolName(string capabilityId)
    {
        var name = new string(capabilityId.Select(value => char.IsLetterOrDigit(value) || value is '_' or '-'
            ? value : '_').ToArray());
        return name.Length <= 64 ? name : name[..64];
    }

    private static InteractionContractException Failure(string code, string message) => new(code, message);
}

internal sealed record SystemInnerWorkerHostSelection(
    AiAgentProfile Profile,
    AiRequest Configuration,
    IReadOnlyList<SystemInnerWorkerToolBinding> ToolBindings,
    SystemInnerWorkerAiBudget AiBudget);

internal interface ISystemInnerWorkerProcedureResolver
{
    Task<SystemInnerWorkerProcedurePreparationResult> ResolveAsync(
        SystemInnerWorkerRequest worker, CancellationToken cancellationToken = default);
}

/// <summary>Builds an exact fresh preparation without dispatching or consuming durable allowance.</summary>
internal sealed class SystemInnerWorkerProcedureResolver(
    IInteractionEnvelopeFactory envelopes,
    IInteractionTaskContextMaterializer contextMaterializer,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    SystemInnerWorkerPreparation preparation,
    SystemInnerWorkerHostPolicy policy,
    TimeProvider time) : ISystemInnerWorkerProcedureResolver
{
    public async Task<SystemInnerWorkerProcedurePreparationResult> ResolveAsync(
        SystemInnerWorkerRequest worker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        var assignment = SystemInnerWorkerAssignmentV1.Parse(worker.InputJson);
        var host = worker.InvocationHost;
        if (worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow)
            throw Failure("INNER_WORKER_SUBJECT_UNSUPPORTED", "Only procedure workflow assignments use this runtime.");
        if (host.Profile != InteractionExecutionProfile.Workflow || host.StateSpaceId is null || host.StateRevision is null)
            throw Failure("INNER_WORKER_SCOPE_INVALID", "A focused procedure worker requires an exact workflow state scope.");
        var intentJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            idempotencyKey = host.CommandId,
            intentText = assignment.Instruction,
            maximumPlanSteps = Math.Min(host.Budget.MaximumOperations, InteractionContractLimits.ProposalSteps)
        }));
        var envelope = envelopes.Create(host.Principal, host.ApplicationRevision.ApplicationId, host.StateSpaceId,
            SessionId(host.CommandId), intentJson, InteractionAiRole.Inner, parentDelegationId: host.ParentCommandId);
        var authorization = new InteractionAuthorizationRequest(host.Principal, host.ApplicationRevision.ApplicationId,
            host.StateSpaceId, InteractionCapability.Plan, CorrelationId(host.CommandId));
        var toolContext = new SystemCapabilityInvocationContext(host.Principal, "inner.procedure", CorrelationId(host.CommandId))
        {
            ApplicationId = host.ApplicationRevision.ApplicationId,
            StateSpaceId = host.StateSpaceId,
            ResolutionFingerprint = envelope.Host.ResolutionFingerprint
        };
        var selection = policy.Resolve(worker, toolContext, time.GetUtcNow().UtcDateTime);
        var procedureContractFingerprint = ResolveProcedureContractFingerprint(worker, envelope);
        var context = await contextMaterializer.MaterializeAsync(envelope, authorization, cancellationToken);
        var prepared = await preparation.PrepareAsync(new(worker, selection.Profile, selection.Configuration,
            envelope, authorization, context.SourceReferences,
            selection.ToolBindings.Select(value => value.Definition.Name).ToArray(), worker.ResultSchemaJson,
            procedureContractFingerprint), cancellationToken);
        if (prepared.ContextFingerprint != context.Fingerprint)
            throw Failure("INNER_WORKER_CONTEXT_CHANGED", "The focused worker context changed while its exact selection was prepared.");
        var profileVersion = new SystemTaskSelectedDefinition(selection.Profile.Id, 1, HashProfile(prepared.Profile));
        var contextEvidence = new SystemInnerWorkerManualContextEvidence(
            "task-context." + context.Fingerprint.ToLowerInvariant(), context.Fingerprint);
        return new(worker, prepared, profileVersion, selection.ToolBindings, contextEvidence,
            selection.AiBudget, toolContext);
    }

    private string ResolveProcedureContractFingerprint(SystemInnerWorkerRequest worker,
        AuthorizedInteractionEnvelope envelope)
    {
        var selected = worker.ProcedureVersion!;
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot)
            || snapshot.EffectiveSetFingerprint != envelope.Host.EffectiveSetFingerprint
            || (snapshot.Resolution?.Fingerprint ?? snapshot.Manifest.Fingerprint) != envelope.Host.ResolutionFingerprint)
            throw Failure("INNER_WORKER_CATALOG_UNAVAILABLE", "The focused worker's active catalog is unavailable or stale.");
        var record = snapshot.Documents.SingleOrDefault(value => value.Trust == SourceTrust.Trusted
            && value.Record.Kind == "procedure" && value.Record.Status == "active"
            && value.Record.QualifiedId == selected.ExactDefinitionId
            && value.Record.Version == selected.Version
            && value.Record.ContentFingerprint == selected.Fingerprint)?.Record;
        if (record is null)
            throw Failure("INNER_WORKER_PROCEDURE_STALE", "The selected procedure is not the exact active catalog winner.");
        try
        {
            using var content = JsonDocument.Parse(record.ContentJson);
            string Required(string name) => content.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new JsonException();
            var status = Required("status") switch
            {
                "active" => DantesRoleplay.Procedures.ProcedureStatus.Active,
                "deprecated" => DantesRoleplay.Procedures.ProcedureStatus.Deprecated,
                "archived" => DantesRoleplay.Procedures.ProcedureStatus.Archived,
                _ => throw new JsonException()
            };
            return ContentHash.ForProcedure(Required("category"), Required("name"), Required("description"),
                Required("governs"), Required("instructions"), Required("constraints"), status);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw Failure("INNER_WORKER_PROCEDURE_INVALID", "The active procedure contract cannot be materialized safely.");
        }
    }

    private static string SessionId(string commandId) => "inner." + Hash(commandId)[..32].ToLowerInvariant();
    private static string CorrelationId(string commandId) => "inner:" + Hash(commandId)[..32].ToLowerInvariant();
    private static string HashProfile(AiAgentProfile profile) => Hash(InteractionCanonicalJson.CanonicalizeObject(
        JsonSerializer.Serialize(new { profile.Id, profile.Name, profile.Identity, profile.Instructions })));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static InteractionContractException Failure(string code, string message) => new(code, message);
}
