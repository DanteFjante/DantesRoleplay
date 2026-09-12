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
    SystemCapabilityInvocationContext ToolContext,
    IReadOnlyList<SystemInnerWorkerApplicationToolSelection> ApplicationTools)
{
    internal SystemInnerWorkerResolvedProfile BindAuthority(SystemInnerWorkerAuthorityProvenance authority) => new(
        Worker, ProfileVersion, Prepared.Profile, Prepared.OutputSchemaFingerprint, ToolBindings,
        Prepared.SelectedContextReferences, ContextEvidence, authority, AiBudget);
}

internal sealed record SystemInnerWorkerApplicationToolSelection(
    SystemInnerWorkerToolBinding Binding,
    CatalogRecordDefinition Record);

internal sealed record SystemInnerWorkerProcedureContract(
    SystemInnerWorkerTrustedActiveProcedure ActiveProcedure,
    ActiveCatalogFeatureSnapshot Snapshot);

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
        SystemCapabilityInvocationContext context, DateTime nowUtc,
        IReadOnlyList<SystemInnerWorkerApplicationToolSelection>? applicationTools = null)
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
        var systemBindings = descriptors.Select(value => new SystemInnerWorkerToolBinding(
            new(ToolName(value.Id),
                value.Mode == SystemCapabilityMode.Read
                    ? $"Read the in-process system capability '{value.Contract.Id}'. {value.Contract.Description}"
                    : $"Write through the in-process system capability '{value.Contract.Id}'. Trusted confirmation and an idempotency token are required. {value.Contract.Description}",
                value.Contract.Input.SchemaJson),
            new(value.Id, value.Version, value.Fingerprint), value.Mode,
            SystemInnerWorkerToolKind.SystemCapability)).ToArray();
        var selectedApplicationTools = applicationTools?.ToArray() ?? [];
        var bindings = systemBindings.Concat(selectedApplicationTools.Select(value => value.Binding)).ToArray();
        if (bindings.Length > 16 || bindings.Select(value => value.Definition.Name)
                .Distinct(StringComparer.Ordinal).Count() != bindings.Length)
            throw Failure("INNER_WORKER_TOOL_SELECTION_INVALID",
                "The focused worker tool selection is ambiguous or exceeds its closed bound.");
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
        return new(profile, configuration, bindings, budget, selectedApplicationTools);
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
    SystemInnerWorkerAiBudget AiBudget,
    IReadOnlyList<SystemInnerWorkerApplicationToolSelection> ApplicationTools);

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
        var procedureContract = ResolveProcedureContract(worker, envelope);
        var applicationTools = ResolveApplicationTools(worker, procedureContract);
        var selection = policy.Resolve(worker, toolContext, time.GetUtcNow().UtcDateTime, applicationTools);
        var context = await contextMaterializer.MaterializeAsync(envelope, authorization, cancellationToken);
        var prepared = await preparation.PrepareAsync(new(worker, selection.Profile, selection.Configuration,
            envelope, authorization, context.SourceReferences,
            selection.ToolBindings.Select(value => value.Definition.Name).ToArray(), worker.ResultSchemaJson,
            procedureContract.ActiveProcedure.ContractFingerprint,
            procedureContract.ActiveProcedure), cancellationToken);
        var currentProcedureContract = ResolveProcedureContract(worker, envelope);
        if (currentProcedureContract.ActiveProcedure != procedureContract.ActiveProcedure)
            throw Failure("INNER_WORKER_PROCEDURE_STALE",
                "The selected procedure changed while its exact worker request was prepared.");
        if (prepared.ContextFingerprint != context.Fingerprint)
            throw Failure("INNER_WORKER_CONTEXT_CHANGED", "The focused worker context changed while its exact selection was prepared.");
        var profileVersion = new SystemTaskSelectedDefinition(selection.Profile.Id, 1, HashProfile(prepared.Profile));
        var contextEvidence = new SystemInnerWorkerManualContextEvidence(
            "task-context." + context.Fingerprint.ToLowerInvariant(), context.Fingerprint);
        return new(worker, prepared, profileVersion, selection.ToolBindings, contextEvidence,
            selection.AiBudget, toolContext, selection.ApplicationTools);
    }

    private SystemInnerWorkerProcedureContract ResolveProcedureContract(SystemInnerWorkerRequest worker,
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
            var governs = Required("governs");
            var category = Required("category");
            var name = Required("name");
            var description = Required("description");
            var instructions = Required("instructions");
            var constraints = Required("constraints");
            var contractFingerprint = ContentHash.ForProcedure(category, name, description,
                governs, instructions, constraints, status);
            var activeProcedure = new SystemInnerWorkerTrustedActiveProcedure(selected,
                snapshot.EffectiveSetFingerprint,
                snapshot.Resolution?.Fingerprint ?? snapshot.Manifest.Fingerprint,
                TrustedSource: true, status, category, name, description, governs, instructions,
                constraints, contractFingerprint);
            return new(activeProcedure, snapshot);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw Failure("INNER_WORKER_PROCEDURE_INVALID", "The active procedure contract cannot be materialized safely.");
        }
    }

    private static IReadOnlyList<SystemInnerWorkerApplicationToolSelection> ResolveApplicationTools(
        SystemInnerWorkerRequest worker, SystemInnerWorkerProcedureContract procedure)
    {
        var references = SystemInnerWorkerGovernedReferences.Parse(procedure.ActiveProcedure.Governs);
        var result = new List<SystemInnerWorkerApplicationToolSelection>();
        foreach (var reference in references)
        {
            var kind = reference.Kind == SystemInnerWorkerGovernedReferenceKind.Action
                ? "mechanic" : ApplicationQueryContract.CatalogKind;
            var matches = procedure.Snapshot.Documents.Where(value => value.Trust == SourceTrust.Trusted
                && value.Record.Kind == kind && value.Record.Status == "active"
                && value.Record.QualifiedId == reference.QualifiedId).ToArray();
            if (matches.Length == 0) continue;
            if (matches.Length != 1)
                throw Failure("INNER_WORKER_TOOL_SELECTION_AMBIGUOUS",
                    "The governed application tool does not have one exact active winner.");
            var record = matches[0].Record;
            var contract = ApplicationCapabilityContractAdapter.Create(
                worker.InvocationHost.ApplicationRevision.ApplicationId, record,
                worker.InvocationHost.StateSpaceId);
            var toolKind = reference.Kind == SystemInnerWorkerGovernedReferenceKind.Action
                ? SystemInnerWorkerToolKind.ApplicationAction : SystemInnerWorkerToolKind.ApplicationQuery;
            var mode = toolKind == SystemInnerWorkerToolKind.ApplicationAction
                ? SystemCapabilityMode.Write : SystemCapabilityMode.Read;
            var definition = SystemInnerWorkerApplicationToolFactory.Definition(toolKind, contract);
            result.Add(new(new(definition, new(record.QualifiedId, record.Version,
                record.ContentFingerprint), mode, toolKind), record));
        }
        if (result.Count > 16)
            throw Failure("INNER_WORKER_TOOL_SELECTION_INVALID",
                "The procedure governs more application tools than the focused worker can admit.");
        return Array.AsReadOnly(result.ToArray());
    }

    private static string SessionId(string commandId) => "inner." + Hash(commandId)[..32].ToLowerInvariant();
    private static string CorrelationId(string commandId) => "inner:" + Hash(commandId)[..32].ToLowerInvariant();
    private static string HashProfile(AiAgentProfile profile) => Hash(InteractionCanonicalJson.CanonicalizeObject(
        JsonSerializer.Serialize(new { profile.Id, profile.Name, profile.Identity, profile.Instructions })));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static InteractionContractException Failure(string code, string message) => new(code, message);
}
