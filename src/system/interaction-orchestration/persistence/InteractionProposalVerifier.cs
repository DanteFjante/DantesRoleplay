using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using System.Text.Json.Nodes;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class InteractionProposalVerifier(
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IProjectionDefinitionRegistry? projections = null) : IInteractionProposalVerifier
{
    public InteractionResolutionResult Verify(InteractionProposalVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Envelope is null || request.Inspected is null || request.Draft is null
            || request.Draft.Steps is null
            || request.Draft.Steps.Count is < 1 or > InteractionContractLimits.ProposalSteps
            || request.Draft.Steps.Any(value => value is null
                || string.IsNullOrWhiteSpace(value.QualifiedId)
                || value.RoleBindings is null || value.DependsOn is null
                || string.IsNullOrWhiteSpace(value.InputJson)))
            return Unsafe("PROPOSAL_DRAFT_INVALID", "The proposed plan is malformed or unbounded.");
        var envelope = request.Envelope;
        var currentApplication = applications.Get(envelope.Host.ApplicationRevision.ApplicationId);
        if (currentApplication is null
            || currentApplication.Revision != envelope.Host.ApplicationRevision.Revision
            || currentApplication.Fingerprint != envelope.Host.ApplicationRevision.Fingerprint
            || !currentApplication.BaseApplications.SequenceEqual(envelope.Host.ApplicationRevision.BaseApplications))
            return Stale("APPLICATION_REVISION_STALE", "The authorized application revision changed during planning.");

        var activation = activations.Current(envelope.Host.ApplicationRevision.ApplicationId);
        if (activation is null || activation.ActivationFingerprint != envelope.Host.EffectiveSetFingerprint)
            return Stale("EFFECTIVE_SET_STALE", "The active application feature set changed during planning.");
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot))
            return Stale("CATALOG_SNAPSHOT_STALE", "The active application catalog is unavailable.");

        var trustedCurrent = snapshot.Documents
            .Where(value => value.Trust == SourceTrust.Trusted)
            .ToDictionary(value => value.Record.QualifiedId, value => value.Record, StringComparer.Ordinal);
        var inspected = new Dictionary<string, CatalogRecordDefinition>(StringComparer.Ordinal);
        foreach (var item in request.Inspected)
        {
            if (item is null || item.Hit is null || item.Hit.Reference is null || item.ContractJson is null
                || item.Hit.Reference.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId
                || item.Hit.Reference.Lane != InteractionRetrievalLane.TrustedFeature
                || item.Hit.Reference.CatalogFingerprint != snapshot.Manifest.Fingerprint
                || !trustedCurrent.TryGetValue(item.Hit.Reference.QualifiedId, out var record)
                || record.Version != item.Hit.Reference.Version
                || record.ContentFingerprint != item.Hit.Reference.ContentFingerprint
                || record.ContentJson != item.ContractJson)
                return Stale("INSPECTED_CONTRACT_STALE", "An inspected contract is no longer the exact current trusted record.");
            if (!inspected.TryAdd(record.QualifiedId, record))
                return Unsafe("DUPLICATE_INSPECTION", "A contract inspection was duplicated.");
        }

        var steps = new List<InteractionPlanStep>();
        var querySchemas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var draft in request.Draft.Steps)
        {
            if (!inspected.TryGetValue(draft.QualifiedId, out var record))
                return Unsafe("CONTRACT_NOT_INSPECTED", "Every proposed contract must be inspected in the current planning trace.");
            if (record.Version != draft.Version || record.ContentFingerprint != draft.Fingerprint)
                return Stale("PROPOSAL_CONTRACT_STALE", "A proposed contract reference does not match the inspected current record.");
            if (record.Status != "active")
                return Unsupported("CONTRACT_NOT_ACTIVE", "The proposed contract is not active.");
            if (record.Kind == "procedure")
                return Unsupported("CONTRACT_KIND_UNSUPPORTED", "Procedure prose is not an executable interaction contract.");
            if ((draft.Kind == InteractionPlanStepKind.Query) != (record.Kind == ApplicationQueryContract.CatalogKind)
                || (draft.Kind == InteractionPlanStepKind.Action) != (record.Kind == "mechanic"))
                return Unsupported("CONTRACT_KIND_UNSUPPORTED", "The proposed step does not match a supported contract kind.");

            string authoritativeId;
            IReadOnlySet<string> declaredRoles;
            IReadOnlySet<string> requiredRoles;
            InteractionQueryContractReference? queryContract = null;
            if (draft.Kind == InteractionPlanStepKind.Query)
            {
                ApplicationQueryContract query;
                try
                {
                    query = ApplicationQueryContract.Parse(record.ContentJson,
                        envelope.Host.ApplicationRevision.ApplicationId);
                }
                catch (Exception exception) when (exception is ArgumentException or JsonException)
                {
                    return Unsafe("QUERY_CONTRACT_INVALID", "The active query contract is malformed.");
                }
                if (query.Status != "active")
                    return Unsupported("QUERY_CONTRACT_NOT_ACTIVE", "The proposed query contract is not active.");
                var effectiveOutputSchemaHash = query.OutputSchemaHash;
                IReadOnlySet<string>? objectRequiredRoles = null;
                if (query.Executor == ApplicationQueryContract.ProjectionExecutor)
                {
                    var projection = projections?.Get(query.ProjectionQualifiedId, query.ProjectionVersion);
                    if (projection is null)
                        return Unsupported("QUERY_EXECUTOR_UNAVAILABLE", "The query projection executor is unavailable.");
                    if (projection.Owner != envelope.Host.ApplicationRevision.ApplicationId
                        || projection.ContentHash != query.ProjectionContentHash
                        || projection.OutputSchemaHash != query.OutputSchemaHash
                        || !JsonNode.DeepEquals(JsonNode.Parse(projection.OutputSchemaJson), JsonNode.Parse(query.OutputSchemaJson))
                        || !projection.EntityRoles.Order(StringComparer.Ordinal)
                            .SequenceEqual(query.Roles.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                        return Stale("QUERY_PROJECTION_STALE", "The query contract does not match its exact registered projection.");
                }
                else if (query.Executor == ApplicationQueryContract.ObjectProjectionExecutor)
                {
                    var projection = projections?.Get(query.ProjectionQualifiedId, query.ProjectionVersion);
                    if (projection?.ObjectContract is null)
                        return Unsupported("QUERY_EXECUTOR_UNAVAILABLE", "The query object executor is unavailable.");
                    if (projection.Owner != envelope.Host.ApplicationRevision.ApplicationId
                        || projection.ContentHash != query.ProjectionContentHash
                        || !JsonNode.DeepEquals(JsonNode.Parse(projection.OutputSchemaJson), JsonNode.Parse(query.OutputSchemaJson))
                        || !projection.EntityRoles.Order(StringComparer.Ordinal)
                            .SequenceEqual(query.Roles.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                        || query.ObjectCollectionId is null
                        || !projection.ObjectContract.Collections.Any(value => value.CollectionId == query.ObjectCollectionId))
                        return Stale("QUERY_PROJECTION_STALE", "The query contract does not match its exact registered object.");
                    effectiveOutputSchemaHash = projection.OutputSchemaHash;
                    objectRequiredRoles = projection.ObjectContract.Roles.Where(value => value.Required)
                        .Select(value => value.RoleId).ToHashSet(StringComparer.Ordinal);
                }
                else if (query.Executor == ApplicationQueryContract.MechanicProjectionExecutor)
                {
                    if (!trustedCurrent.TryGetValue(query.ProjectionQualifiedId, out var mechanic)
                        || mechanic.Kind != "mechanic" || mechanic.Status != "active"
                        || mechanic.Version != query.ProjectionVersion
                        || mechanic.ContentFingerprint != query.ProjectionContentHash)
                        return Stale("QUERY_PROJECTION_STALE", "The query does not pin an exact active trusted mechanic.");
                    try
                    {
                        using var document = JsonDocument.Parse(mechanic.ContentJson);
                        var requirements = MechanicRequirements.Parse(document.RootElement.GetProperty("requirements").GetString()!);
                        if (requirements.Event is not null || requirements.ProjectionProblems().Count > 0
                            || requirements.CompositionProblems().Count > 0 || requirements.Roles is null
                            || !requirements.Roles.Keys.Order(StringComparer.Ordinal)
                                .SequenceEqual(query.Roles.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                            return Unsafe("QUERY_PROJECTION_INVALID", "The mechanic projection requirements do not match the query.");
                    }
                    catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
                    {
                        return Unsafe("QUERY_PROJECTION_INVALID", "The mechanic projection requirements are invalid.");
                    }
                }
                else
                    return Unsupported("QUERY_EXECUTOR_UNAVAILABLE", "The query executor is unavailable.");
                if (InteractionCanonicalJson.CanonicalizeObject(draft.InputJson) != "{}")
                    return Unsafe("QUERY_INPUT_FORBIDDEN", "Projection queries do not accept free-form input.");
                authoritativeId = query.Id;
                declaredRoles = query.Roles.Keys.ToHashSet(StringComparer.Ordinal);
                requiredRoles = objectRequiredRoles ?? declaredRoles;
                try
                {
                    queryContract = new(query.Executor, query.ProjectionQualifiedId, query.ProjectionVersion,
                        query.ProjectionContentHash, effectiveOutputSchemaHash, query.OutputSchemaJson,
                        query.Exposure, query.Roles.Keys);
                }
                catch (InteractionContractException)
                {
                    return Unsafe("QUERY_CONTRACT_INVALID", "The active query contract exceeds the closed interaction bounds.");
                }
            }
            else
            {
                MechanicRequirements requirements;
                try
                {
                    using var document = JsonDocument.Parse(record.ContentJson);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(id.GetString())
                        || !root.TryGetProperty("requirements", out var requirementsElement)
                        || requirementsElement.ValueKind != JsonValueKind.String)
                        return Unsafe("MECHANIC_CONTRACT_INVALID", "The active mechanic contract is malformed.");
                    authoritativeId = id.GetString()!;
                    requirements = MechanicRequirements.Parse(requirementsElement.GetString()!);
                }
                catch (JsonException)
                {
                    return Unsafe("MECHANIC_CONTRACT_INVALID", "The active mechanic requirements are malformed.");
                }
                if (requirements.Roles is null || requirements.Children is null)
                    return Unsafe("MECHANIC_REQUIREMENTS_INVALID", "The active mechanic declares invalid generic requirements.");
                if (requirements.Event is not null)
                    return Unsupported("EVENT_MECHANIC_NOT_DIRECT", "An event middleware mechanic cannot be proposed as a direct action.");
                if (requirements.ProjectionProblems().Count > 0 || requirements.CompositionProblems().Count > 0)
                    return Unsafe("MECHANIC_REQUIREMENTS_INVALID", "The active mechanic declares invalid generic requirements.");
                declaredRoles = requirements.Roles.Keys.ToHashSet(StringComparer.Ordinal);
                requiredRoles = requirements.Roles.Where(value => !value.Value.Optional)
                    .Select(value => value.Key).ToHashSet(StringComparer.Ordinal);
            }

            var resultBindings = draft.ResultBindings ?? [];
            var roleHintProblem = ValidateRoleHints(envelope.Intent.RoleHints, draft, resultBindings);
            if (roleHintProblem is not null) return roleHintProblem;
            var bindingProblem = ValidateResultBindings(draft, resultBindings, querySchemas, declaredRoles);
            if (bindingProblem is not null) return bindingProblem;
            var supplied = draft.RoleBindings.Keys.Concat(resultBindings
                .Where(binding => binding.ToRole is not null).Select(binding => binding.ToRole!))
                .ToHashSet(StringComparer.Ordinal);
            var unknown = supplied.Except(declaredRoles, StringComparer.Ordinal).Order(StringComparer.Ordinal).FirstOrDefault();
            if (unknown is not null)
                return Unsafe("UNKNOWN_CONTRACT_ROLE", "The proposal supplies a role that the contract does not declare.");
            var missing = requiredRoles.Where(role => !supplied.Contains(role)).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                return InteractionResolutionResult.NonResolution(
                    InteractionResolutionStatus.NeedsInput,
                    "MISSING_REQUIRED_ROLE",
                    "The selected contract requires additional role bindings.",
                    missing.Select(value => "role:" + value));
            InteractionContractReference reference;
            try
            {
                reference = new(
                    InteractionFeatureScope.Application,
                    envelope.Host.ApplicationRevision.ApplicationId,
                    record.QualifiedId,
                    authoritativeId,
                    record.Version,
                    record.ContentFingerprint);
                steps.Add(new(
                    draft.StepId,
                    draft.Kind,
                    reference,
                    draft.DependsOn,
                    draft.RoleBindings,
                    draft.InputJson,
                    envelope.Host.StateRevision,
                    resultBindings,
                    queryContract));
                if (queryContract is not null)
                    querySchemas.Add(draft.StepId, queryContract.OutputSchemaJson);
            }
            catch (InteractionContractException exception)
            {
                return exception.Code.Contains("STALE", StringComparison.Ordinal)
                    ? Stale(exception.Code, "The proposed plan no longer matches current authority.")
                    : Unsafe(exception.Code, "The proposed plan violates the closed interaction contract.");
            }
        }

        try
        {
            return InteractionResolutionResult.Resolved(InteractionProposal.Create(envelope, steps));
        }
        catch (InteractionContractException exception)
        {
            return exception.Code.Contains("STALE", StringComparison.Ordinal)
                ? Stale(exception.Code, "The proposed plan no longer matches current authority.")
                : Unsafe(exception.Code, "The proposed plan violates the closed interaction contract.");
        }
    }

    private static InteractionResolutionResult? ValidateRoleHints(
        IReadOnlyDictionary<string, string> roleHints,
        InteractionPlannerDraftStep draft,
        IReadOnlyList<InteractionResultBinding> resultBindings)
    {
        foreach (var hint in roleHints)
        {
            if (draft.RoleBindings.TryGetValue(hint.Key, out var supplied) && supplied != hint.Value)
                return Unsafe("ROLE_HINT_BINDING_MISMATCH",
                    "A proposed role binding conflicts with the host-authorized role reference.");
        }

        if (resultBindings.Any(binding => binding.ToRole is not null && roleHints.ContainsKey(binding.ToRole)))
            return Unsafe("ROLE_HINT_RESULT_BINDING_FORBIDDEN",
                "A proposed result binding cannot replace a host-authorized role reference.");

        return null;
    }

    private static InteractionResolutionResult? ValidateResultBindings(
        InteractionPlannerDraftStep draft,
        IReadOnlyList<InteractionResultBinding> bindings,
        IReadOnlyDictionary<string, string> querySchemas,
        IReadOnlySet<string> declaredRoles)
    {
        if (bindings.Count > InteractionContractLimits.ResultBindingsPerStep
            || bindings.Select(binding => binding.TargetKey).Distinct(StringComparer.Ordinal).Count() != bindings.Count)
            return Unsafe("RESULT_BINDINGS_INVALID", "Result bindings are duplicated or unbounded.");
        var inputTargets = bindings.Where(binding => binding.ToInputPointer is not null)
            .Select(binding => binding.ToInputPointer!).Order(StringComparer.Ordinal).ToArray();
        if (inputTargets.SelectMany((value, index) => inputTargets.Skip(index + 1)
                .Where(other => Overlaps(value, other))).Any())
            return Unsafe("RESULT_BINDING_TARGET_OVERLAP", "Result binding input targets overlap.");
        using var inputDocument = JsonDocument.Parse(draft.InputJson);
        foreach (var binding in bindings)
        {
            if (!draft.DependsOn.Contains(binding.FromStepId, StringComparer.Ordinal)
                || !querySchemas.TryGetValue(binding.FromStepId, out var schema))
                return Unsafe("RESULT_BINDING_SOURCE_INVALID", "A result binding must name an earlier query dependency.");
            if (!ProjectionSchemaPath.Exists(schema, binding.FromPointer))
                return Unsafe("RESULT_BINDING_SOURCE_PATH_INVALID", "A result binding source path is absent from the exact query schema.");
            if (binding.ToRole is not null)
            {
                if (!declaredRoles.Contains(binding.ToRole) || draft.RoleBindings.ContainsKey(binding.ToRole))
                    return Unsafe("RESULT_BINDING_ROLE_INVALID", "A result binding role is unknown or already supplied statically.");
                continue;
            }
            if (!AvailableInputTarget(inputDocument.RootElement, binding.ToInputPointer!))
                return Unsafe("RESULT_BINDING_INPUT_TARGET_INVALID", "A result binding input target is absent, occupied, or unsupported.");
        }
        return null;
    }

    private static bool AvailableInputTarget(JsonElement input, string pointer)
    {
        if (pointer == "") return !input.EnumerateObject().Any();
        var tokens = pointer.Split('/').Skip(1).Select(Decode).ToArray();
        var current = input;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(tokens[index], out current)
                || current.ValueKind != JsonValueKind.Object)
                return false;
        }
        return current.ValueKind == JsonValueKind.Object && !current.TryGetProperty(tokens[^1], out _);
    }

    private static string Decode(string value) => value.Replace("~1", "/", StringComparison.Ordinal)
        .Replace("~0", "~", StringComparison.Ordinal);

    private static bool Overlaps(string left, string right) => left == "" || right == ""
        || left.StartsWith(right + "/", StringComparison.Ordinal)
        || right.StartsWith(left + "/", StringComparison.Ordinal);

    private static InteractionResolutionResult Stale(string code, string summary) =>
        InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Stale, code, summary, []);

    private static InteractionResolutionResult Unsafe(string code, string summary) =>
        InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Unsafe, code, summary, []);

    private static InteractionResolutionResult Unsupported(string code, string summary) =>
        InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Unsupported, code, summary, []);
}
