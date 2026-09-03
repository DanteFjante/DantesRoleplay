using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Capabilities;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Web.Interactions;

public sealed class ApplicationMechanicWebException : Exception
{
    public ApplicationMechanicWebException(
        string code, string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed record ApplicationMechanicComponentContractView(
    string LocalId,
    string QualifiedId,
    int Version,
    string ProfileId,
    string SchemaJson,
    string SchemaHash);

public sealed record ApplicationMechanicComponentReferenceView(
    string SourceComponentId,
    string Field,
    IReadOnlyList<ApplicationMechanicComponentContractView> Targets);

public sealed record ApplicationMechanicRoleView(
    string Name,
    bool Required,
    string Description,
    bool IncludeContents,
    int? ContentsDepth,
    bool IncludeRelationships,
    IReadOnlyList<ApplicationMechanicComponentContractView> Components,
    IReadOnlyList<ApplicationMechanicComponentContractView> ContentComponents,
    IReadOnlyList<ApplicationMechanicComponentReferenceView> ComponentReferences);

public sealed record ApplicationMechanicInputContractView(
    string Shape,
    string ValidationOwner,
    string SchemaStatus,
    string? SchemaJson);

public sealed record ApplicationMechanicDescriptorView(
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string QualifiedMechanicId,
    string AuthoritativeId,
    string Name,
    string Description,
    int Version,
    string ContentFingerprint,
    bool RequiresConfirmation,
    ApplicationMechanicInputContractView Input,
    IReadOnlyList<ApplicationMechanicRoleView> Roles,
    CapabilityContractDescriptor Capability);

public sealed record ApplicationMechanicPrepareRequest(
    string? IdempotencyKey,
    Dictionary<string, string>? RoleEntityIds,
    JsonElement Input);

public sealed record ApplicationMechanicPreparationView(
    bool Ready,
    string Status,
    string Code,
    string SafeSummary,
    IReadOnlyList<string> Evidence,
    bool RequiresConfirmation,
    string? ProposalFingerprint,
    InteractionProposalProjection? Proposal,
    InteractionReceiptProjection Receipt);

public sealed record ApplicationMechanicExecuteRequest(
    string? ResolutionReceiptId,
    string? ProposalFingerprint,
    string? IdempotencyKey,
    JsonElement Proposal);

public sealed class ApplicationMechanicWebService(
    IStateSpaceRegistry stateSpaces,
    IApplicationActivationReader activations,
    IApplicationComponentTypeRegistry componentTypes,
    IInteractionGateway interactions,
    IBoundedJsonSchemaValidator schemas)
{
    private const string SessionContextId = "web.direct-application-action";

    public async Task<ApplicationMechanicDescriptorView> DescribeAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string qualifiedMechanicId,
        CancellationToken cancellationToken = default)
    {
        var stateSpace = CurrentScope(applicationId, stateSpaceId, qualifiedMechanicId);
        var result = await interactions.SearchFeaturesAsync(applicationId, null,
            qualifiedMechanicId, 1, cancellationToken: cancellationToken);
        var hit = result.Hits.SingleOrDefault(value => value.Exact
            && value.Reference.Kind == "mechanic"
            && value.Reference.QualifiedId == qualifiedMechanicId);
        if (hit is null)
            throw Failure("MECHANIC_UNKNOWN", "The exact trusted application mechanic is unavailable.", 404);

        MechanicRequirements requirements;
        string authoritativeId;
        try
        {
            using var document = JsonDocument.Parse(hit.ContractJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(id.GetString())
                || !root.TryGetProperty("requirements", out var declared)
                || declared.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("status", out var status) || status.GetString() != "active")
                throw new JsonException();
            authoritativeId = id.GetString()!;
            requirements = MechanicRequirements.Parse(declared.GetString()!);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Failure("MECHANIC_CONTRACT_INVALID",
                "The exact application mechanic contract is invalid.", 409, exception);
        }
        if (requirements.Roles is null || requirements.Children is null
            || requirements.ProjectionProblems().Count > 0
            || requirements.CompositionProblems().Count > 0)
            throw Failure("MECHANIC_REQUIREMENTS_INVALID",
                "The exact application mechanic requirements are invalid.", 409);
        if (requirements.Event is not null)
            throw Failure("EVENT_MECHANIC_NOT_DIRECT",
                "An event middleware mechanic is not a direct application action.", 422);

        var owners = stateSpace.ApplicationRevision.BaseApplications
            .Prepend(stateSpace.ApplicationRevision.ApplicationId).ToArray();
        var roles = requirements.Roles.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => Role(value.Key, value.Value, owners)).ToArray();
        var capability = ApplicationCapabilityContractAdapter.CreateMechanic(
            applicationId, hit.Reference.QualifiedId, hit.Name, hit.Description,
            hit.Reference.Version, hit.Reference.ContentFingerprint, "active",
            hit.ContractJson, stateSpaceId);
        return new(applicationId, stateSpaceId, hit.Reference.QualifiedId, authoritativeId,
            hit.Name, hit.Description, hit.Reference.Version, hit.Reference.ContentFingerprint,
            capability.RequiresConfirmation,
            new("json-object", "mechanic", capability.Input.Status, capability.Input.SchemaJson),
            roles, capability);
    }

    public async Task<ApplicationMechanicPreparationView> PrepareAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string qualifiedMechanicId,
        ApplicationMechanicPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        var descriptor = await DescribeAsync(applicationId, stateSpaceId, qualifiedMechanicId,
            cancellationToken);
        if (request.Input.ValueKind != JsonValueKind.Object)
            throw Failure("APPLICATION_ACTION_INPUT_INVALID",
                "Application action input must be one bounded JSON object.", 400);
        var roles = CopyRoles(request.RoleEntityIds);
        var input = InteractionCanonicalJson.CanonicalizeObject(request.Input.GetRawText());
        var validation = schemas.Validate(descriptor.Capability.Input.SchemaJson, input);
        if (validation.Status != SchemaValueStatus.Valid)
            throw Failure("APPLICATION_ACTION_INPUT_SCHEMA_INVALID",
                "Application action input does not match the current mechanic schema.", 400);
        var idempotencyKey = RequireText(request.IdempotencyKey, "idempotencyKey");
        var intentJson = JsonSerializer.Serialize(new
        {
            idempotencyKey,
            intentText = "Prepare one exact direct application action.",
            roleHints = roles,
            conversationFactReferences = Array.Empty<string>(),
            maximumPlanSteps = 1,
            plannerPreference = "automatic"
        });
        var proposalJson = JsonSerializer.Serialize(new
        {
            command = "propose",
            steps = new[]
            {
                new
                {
                    stepId = "action",
                    kind = "action",
                    qualifiedId = descriptor.QualifiedMechanicId,
                    version = descriptor.Version,
                    fingerprint = descriptor.ContentFingerprint,
                    dependsOn = Array.Empty<string>(),
                    roleBindings = roles,
                    input = JsonSerializer.Deserialize<JsonElement>(input)
                }
            }
        });
        var plan = await interactions.PlanAsync(principal, applicationId, stateSpaceId,
            SessionContextId, intentJson, proposalJson, role: InteractionAiRole.Direct,
            cancellationToken: cancellationToken);
        if (plan.Receipt.Disposition == InteractionReceiptWriteDisposition.Conflict
            || plan.Receipt.Receipt is null)
            throw Failure("INTERACTION_RECEIPT_IDEMPOTENCY_CONFLICT",
                "The preparation idempotency key is already bound to another request.", 409);
        var ready = plan.Status == InteractionResolutionStatus.Resolved
            && plan.Proposal is not null && plan.ProposalFingerprint is not null;
        return new(ready, InteractionResolutionStatusNames.Get(plan.Status), plan.Code,
            plan.SafeSummary, plan.Evidence, true, plan.ProposalFingerprint,
            plan.Proposal, plan.Receipt.Receipt);
    }

    public async Task<InteractionExecutionOutcome> ExecuteAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string qualifiedMechanicId,
        ApplicationMechanicExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        _ = CurrentScope(applicationId, stateSpaceId, qualifiedMechanicId);
        if (request.Proposal.ValueKind != JsonValueKind.Object)
            throw Failure("APPLICATION_ACTION_PROPOSAL_INVALID",
                "Execution requires the exact inert proposal returned by prepare.", 400);
        var proposalJson = InteractionCanonicalJson.CanonicalizeObject(request.Proposal.GetRawText());
        InteractionPlannerProposalCommand proposal;
        try
        {
            proposal = InteractionPlannerCommand.Parse(proposalJson) as InteractionPlannerProposalCommand
                ?? throw new InteractionContractException("PROPOSAL_COMMAND_REQUIRED",
                    "Execution requires one exact proposed action.");
        }
        catch (InteractionContractException exception)
        {
            throw Failure(exception.Code, "The execution proposal is invalid.", 400, exception);
        }
        var step = proposal.Steps.SingleOrDefault();
        if (step is null || step.Kind != InteractionPlanStepKind.Action
            || step.QualifiedId != qualifiedMechanicId || step.DependsOn.Count != 0
            || (step.ResultBindings?.Count ?? 0) != 0)
            throw Failure("APPLICATION_ACTION_PROPOSAL_SCOPE_MISMATCH",
                "The execution proposal does not name the one route mechanic.", 409);
        var descriptor = await DescribeAsync(applicationId, stateSpaceId, qualifiedMechanicId,
            cancellationToken);
        if (step.Version != descriptor.Version || step.Fingerprint != descriptor.ContentFingerprint)
            throw Failure("MECHANIC_CONTRACT_STALE",
                "The prepared mechanic version or content fingerprint is stale.", 409);
        var executionJson = JsonSerializer.Serialize(new
        {
            resolutionReceiptId = RequireText(request.ResolutionReceiptId, "resolutionReceiptId"),
            proposalFingerprint = RequireText(request.ProposalFingerprint, "proposalFingerprint"),
            idempotencyKey = RequireText(request.IdempotencyKey, "idempotencyKey"),
            proposal = JsonSerializer.Deserialize<JsonElement>(proposalJson),
            stopOnFailure = true,
            learn = false
        });
        return await interactions.ExecuteAsync(principal, applicationId, stateSpaceId,
            executionJson, cancellationToken);
    }

    private StateSpaceView CurrentScope(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string qualifiedMechanicId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (!ValidId(stateSpaceId, 200) || !ValidId(qualifiedMechanicId, 200))
            throw Failure("APPLICATION_ACTION_SCOPE_INVALID",
                "The application action scope is invalid.", 400);
        var stateSpace = stateSpaces.Get(stateSpaceId);
        if (stateSpace is null)
            throw Failure("STATE_SPACE_UNKNOWN", "The state space is unavailable.", 404);
        if (stateSpace.ApplicationRevision.ApplicationId != applicationId)
            throw Failure("STATE_SPACE_WRONG_APPLICATION",
                "The state space belongs to another application.", 404);
        var activation = activations.Current(applicationId);
        if (activation is null || activation.ActivationFingerprint != stateSpace.ManifestFingerprint)
            throw Failure("STATE_SPACE_ACTIVATION_STALE",
                "The state space is not bound to the current application activation.", 409);
        return stateSpace;
    }

    private ApplicationMechanicRoleView Role(
        string name,
        RoleRequirement requirement,
        IReadOnlyList<ApplicationIdentifier> owners)
    {
        if (requirement is null || !ValidId(name, 200))
            throw Failure("MECHANIC_REQUIREMENTS_INVALID",
                "The exact application mechanic role declaration is invalid.", 409);
        var components = requirement.Components.Order(StringComparer.Ordinal)
            .Select(value => Contract(value, owners)).ToArray();
        var content = (requirement.ContentComponentIds ?? []).Order(StringComparer.Ordinal)
            .Select(value => Contract(value, owners)).ToArray();
        var references = (requirement.ComponentReferences ?? [])
            .OrderBy(value => value.SourceComponentId, StringComparer.Ordinal)
            .ThenBy(value => value.Field, StringComparer.Ordinal)
            .Select(value => new ApplicationMechanicComponentReferenceView(
                value.SourceComponentId, value.Field,
                value.TargetComponentIds.Order(StringComparer.Ordinal)
                    .Select(target => Contract(target, owners)).ToArray()))
            .ToArray();
        return new(name, !requirement.Optional, requirement.Description,
            requirement.IncludeContents, requirement.IncludeContents
                ? requirement.ContentsDepth ?? 1 : null,
            requirement.IncludeRelationships, components, content, references);
    }

    private ApplicationMechanicComponentContractView Contract(
        string localOrQualifiedId,
        IReadOnlyList<ApplicationIdentifier> owners)
    {
        if (!ValidId(localOrQualifiedId, 200))
            throw Failure("COMPONENT_MAPPING_MISSING",
                "A declared component has no exact current application mapping.", 409);
        RegisteredComponentTypeVersion? resolved = null;
        var explicitOwner = owners.FirstOrDefault(owner =>
            localOrQualifiedId.StartsWith(owner.Value + ".", StringComparison.Ordinal));
        if (explicitOwner is not null)
        {
            var candidate = componentTypes.GetLatest(localOrQualifiedId);
            if (candidate?.Owner == explicitOwner) resolved = candidate;
        }
        else
        {
            foreach (var owner in owners)
            {
                var candidate = componentTypes.GetLatest(owner.Value + "." + localOrQualifiedId);
                if (candidate is null) continue;
                resolved = candidate;
                break;
            }
        }
        if (resolved is null)
            throw Failure("COMPONENT_MAPPING_MISSING",
                "A declared component has no exact current application mapping.", 409);
        return new(localOrQualifiedId, resolved.QualifiedId, resolved.Version,
            resolved.ProfileId, resolved.SchemaJson, resolved.SchemaHash);
    }

    private static IReadOnlyDictionary<string, string> CopyRoles(
        IReadOnlyDictionary<string, string>? values)
    {
        values ??= new Dictionary<string, string>();
        if (values.Count > 32 || values.Any(value =>
                !ValidId(value.Key, 200) || !ValidId(value.Value, 200)))
            throw Failure("APPLICATION_ACTION_ROLES_INVALID",
                "Application action role bindings are invalid or unbounded.", 400);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values) result.Add(value.Key, value.Value);
        return result;
    }

    private static string RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Failure("APPLICATION_ACTION_REQUEST_INVALID", $"{name} is required.", 400);
        return value;
    }

    private static bool ValidId(string? value, int maximum) => value is { Length: >= 1 }
        && value.Length <= maximum && !value.Any(char.IsControl) && value.Trim() == value;

    private static ApplicationMechanicWebException Failure(
        string code, string message, int status, Exception? inner = null) =>
        new(code, message, status, inner);
}
