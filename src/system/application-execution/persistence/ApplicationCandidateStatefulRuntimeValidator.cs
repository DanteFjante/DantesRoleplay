using System.Buffers.Binary;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.StateSpaceAdministration;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationExecution;

internal sealed record ApplicationCandidateStatefulSampleResult(
    StandingGrantDefinitionReference Definition, int SampleIndex, string StateSpaceId,
    string StateRevision, IReadOnlyDictionary<string, string> RoleEntityIds,
    string InputFingerprint, string ExpectedDataFingerprint, string ActualDataFingerprint,
    string ExpectedEffectsFingerprint, string ActualEffectsFingerprint,
    string ProjectionFingerprint, string BatchJson, string BatchFingerprint, string DryRunOperationId);

internal sealed record ApplicationCandidateStatefulRuntimeReport(
    ApplicationCandidateReference Candidate, string UpdateFingerprint,
    string PolicyVersion, string PolicyFingerprint,
    StandingGrantDefinitionReference RootPredecessor,
    IReadOnlyList<StandingGrantDefinitionReference> Dependencies,
    string StateGrantReference, string StateGrantFingerprint,
    IReadOnlyList<string> EffectKinds,
    IReadOnlyList<ApplicationCandidateStatefulSampleResult> Samples);

/// <summary>Runs exact existing atomic body replacements through the real stateful execution owners without committing.</summary>
internal sealed class ApplicationCandidateStatefulRuntimeValidator(
    DantesRoleplayDbContext db,
    IApplicationActivationReader activations,
    IPublicApplicationCatalogProvider catalogs,
    IStateSpaceRegistry stateSpaces,
    IApplicationMechanicProjectionMappingResolver mappings,
    ApplicationMechanicEvaluator evaluator,
    ApplicationActionRunner actions,
    IApplicationEcsEffectApplier effects,
    IStandingGrantReadCandidateReader grantCandidates,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants)
{
    internal const string PolicyVersion = "stateful-atomic-body-runtime-v1";
    internal static readonly string PolicyFingerprint = InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/stateful-atomic-body-runtime-policy/v1",
        "candidate-overlay|active-children|projection-v1|jint-default|retained-typed-batch|ecs-dry-run|current-read-execute-grant");

    internal async Task<ApplicationCandidateStatefulRuntimeReport?> ValidateAsync(
        ApplicationCandidateStatefulUpdateEvidence update,
        IReadOnlyList<ApplicationCandidateValidationSample> samples,
        InteractionInvocationHost authoringHost,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null || samples.Count == 0
            || samples.Any(value => value.Definition != update.Definition || value.StateSpaceId is null
                || value.StateRevision is null || value.RoleEntityIds is null || value.ExpectedEffectsJson is null)
            || !catalogs.TryGet(update.Candidate.ApplicationId, out var activeCatalog)) return null;
        var dependencies = Dependencies(update, activeCatalog);
        if (dependencies is null) return null;
        var overlay = new CandidateCatalogOverlay(activeCatalog, update.Successor);
        StandingGrantRevision? selectedGrant = null;
        var results = new List<ApplicationCandidateStatefulSampleResult>(samples.Count);
        var effectKinds = new SortedSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < samples.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = samples[index];
            var state = stateSpaces.Get(sample.StateSpaceId!);
            if (state is null || !SameRevision(state.ApplicationRevision, authoringHost.ApplicationRevision)
                || state.ManifestFingerprint != update.Basis.ActivationFingerprint
                || InteractionStateRevision.From(state) != sample.StateRevision) return null;
            var grant = await SelectGrantAsync(authoringHost, state, update.PredecessorDefinition, cancellationToken);
            if (grant is null || selectedGrant is not null && (selectedGrant.GrantReference != grant.GrantReference
                    || selectedGrant.ContentFingerprint != grant.ContentFingerprint)) return null;
            selectedGrant = grant;
            var mapping = await mappings.ResolveAsync(state.StateSpaceId, update.Candidate.ApplicationId,
                update.Definition.DefinitionId, update.Requirements, cancellationToken);
            if (!mapping.Resolved) return null;
            var seedMaterial = InteractionCanonicalJson.Fingerprint(
                "dantes-roleplay/stateful-candidate-sample-seed/v1",
                update.Fingerprint + "\n" + index + "\n" + sample.InputJson);
            var seed = BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(seedMaterial[..16]));
            var evaluation = await evaluator.EvaluateCandidateAsync(new(
                state.StateSpaceId, update.Candidate.ApplicationId, update.Definition.DefinitionId,
                update.Definition.ContentFingerprint, mapping.Mapping!, sample.RoleEntityIds!, sample.InputJson, seed),
                overlay, cancellationToken);
            if (!evaluation.Ok || evaluation.Run is null || evaluation.Projection is null
                || evaluation.Proposal.Notifications.Count > 0 || evaluation.Run.Output.Notifications.Count > 0) return null;
            var proposal = evaluation.Proposal.Append(evaluation.Run.Output);
            var built = await actions.BuildEffectBatchAsync(state, mapping.Mapping!, evaluation.Projection,
                update.Requirements, proposal, update.Definition.DefinitionId,
                update.Definition.Revision, seed, cancellationToken);
            if (!built.Ok || built.Batch is null) return null;
            var actualData = InteractionCanonicalJson.CanonicalizeObject(evaluation.Run.Output.Data);
            var actualEffects = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(built.Batch.Effects));
            var batchJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(built.Batch));
            if (actualData != sample.ExpectedDataJson || actualEffects != sample.ExpectedEffectsJson) return null;
            foreach (var effect in built.Batch.Effects) effectKinds.Add(effect.Type);
            var dryRun = await effects.ApplyAsync(built.Batch, dryRun: true, cancellationToken);
            if (!dryRun.Valid || !dryRun.DryRun || dryRun.Applied || dryRun.Replayed) return null;
            results.Add(new(update.Definition, index, state.StateSpaceId, sample.StateRevision!,
                sample.RoleEntityIds!, DataFingerprint(sample.InputJson), DataFingerprint(sample.ExpectedDataJson),
                DataFingerprint(actualData), DataFingerprint(sample.ExpectedEffectsJson!), DataFingerprint(actualEffects),
                DataFingerprint(built.Batch.ProjectionJson),
                batchJson, DataFingerprint(batchJson), dryRun.OperationId));
        }
        if (selectedGrant is null || !effectKinds.All(value => selectedGrant.EffectKinds.Contains(value, StringComparer.Ordinal)))
            return null;
        // Re-evaluate actual current authority with the produced effect kinds. Sample data never supplies authority.
        foreach (var sample in samples)
        {
            var state = stateSpaces.Get(sample.StateSpaceId!)!;
            var stateHost = StateHost(authoringHost, state, selectedGrant.GrantReference);
            var resolved = await targets.ResolveAsync(stateHost, update.PredecessorDefinition, cancellationToken);
            if (resolved is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }) return null;
            foreach (var capability in new[] { StandingGrantCapability.Read, StandingGrantCapability.Execute })
            {
                var decision = await EvaluateAuthorityAsync(stateHost,
                    new(capability, StandingGrantScope.StateSpace, [target],
                        capability == StandingGrantCapability.Execute ? effectKinds.ToArray() : []), cancellationToken);
                if (!decision.Allowed || decision.Grant?.GrantReference != selectedGrant.GrantReference
                    || decision.Grant.ContentFingerprint != selectedGrant.ContentFingerprint) return null;
            }
        }
        return new(update.Candidate, update.Fingerprint, PolicyVersion, PolicyFingerprint,
            update.PredecessorDefinition, dependencies, selectedGrant.GrantReference,
            selectedGrant.ContentFingerprint, effectKinds.ToArray(), results.AsReadOnly());
    }

    internal async Task<bool> CurrentAsync(ApplicationCandidateStatefulUpdateEvidence update,
        ApplicationCandidateStatefulRuntimeReport report, InteractionInvocationHost host,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            return await CurrentCoreAsync(update, report, host, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var current = await CurrentCoreAsync(update, report, host, cancellationToken);
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return current;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<bool> CurrentCoreAsync(ApplicationCandidateStatefulUpdateEvidence update,
        ApplicationCandidateStatefulRuntimeReport report, InteractionInvocationHost host,
        CancellationToken cancellationToken)
    {
        var activation = activations.Current(update.Candidate.ApplicationId);
        if (report.Candidate != update.Candidate || report.UpdateFingerprint != update.Fingerprint
            || report.PolicyVersion != PolicyVersion || report.PolicyFingerprint != PolicyFingerprint
            || report.RootPredecessor != update.PredecessorDefinition || report.Samples.Count == 0
            || activation is null || !catalogs.TryGet(update.Candidate.ApplicationId, out var catalog)
            || Dependencies(update, catalog) is not { } dependencies
            || !dependencies.SequenceEqual(report.Dependencies)) return false;
        var activeDefinition = catalog.Inspect(new(update.Candidate.ApplicationId,
            update.Candidate.ApplicationId.Value, update.Definition.DefinitionId));
        var beforePublication = activation.ActivationFingerprint == update.Basis.ActivationFingerprint
            && activeDefinition.Summary.ContentFingerprint == update.PredecessorDefinition.ContentFingerprint;
        var published = activation.CandidateManifestFingerprint == update.Candidate.ContentFingerprint
            && activation.ApplicationRevision == update.Basis.ApplicationRevision
            && activation.ApplicationFingerprint == update.Basis.ApplicationFingerprint
            && activeDefinition.Summary == update.Successor.Summary;
        if (!beforePublication && !published) return false;
        var authorityDefinition = published ? update.Definition : update.PredecessorDefinition;
        foreach (var sample in report.Samples)
        {
            var state = stateSpaces.Get(sample.StateSpaceId);
            if (state is null || !SameRevision(state.ApplicationRevision, host.ApplicationRevision)
                || beforePublication && (state.ManifestFingerprint != update.Basis.ActivationFingerprint
                    || InteractionStateRevision.From(state) != sample.StateRevision)
                || published && !await IsPublishedRebindingAsync(
                    state, sample.StateRevision, update.Basis, activation, cancellationToken)) return false;
            var candidates = await grantCandidates.ReadAsync(host.Principal, update.Candidate.ApplicationId,
                StandingGrantScope.StateSpace, state.StateSpaceId,
                new HashSet<StandingGrantCapability> { StandingGrantCapability.Read, StandingGrantCapability.Execute }, cancellationToken);
            var grant = candidates.Candidates.SingleOrDefault(value => value.GrantReference == report.StateGrantReference
                && value.ContentFingerprint == report.StateGrantFingerprint);
            if (grant is null || grant.Revoked || grant.ExpiresAtUtc <= DateTime.UtcNow
                || !report.EffectKinds.All(value => grant.EffectKinds.Contains(value, StringComparer.Ordinal))) return false;
            var stateHost = StateHost(host, state, grant.GrantReference);
            var resolved = await targets.ResolveAsync(stateHost, authorityDefinition, cancellationToken);
            if (resolved is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }) return false;
            foreach (var capability in new[] { StandingGrantCapability.Read, StandingGrantCapability.Execute })
                if (!(await grants.EvaluateAsync(stateHost,
                        new(capability, StandingGrantScope.StateSpace, [target],
                            capability == StandingGrantCapability.Execute ? report.EffectKinds : []), cancellationToken)).Allowed)
                    return false;
            var dryRun = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == sample.DryRunOperationId, cancellationToken);
            if (dryRun is null || !DryRunMatches(dryRun, sample, update.Definition)) return false;
        }
        return true;
    }

    private static bool DryRunMatches(Operation operation, ApplicationCandidateStatefulSampleResult sample,
        StandingGrantDefinitionReference definition)
    {
        try
        {
            using var document = JsonDocument.Parse(sample.BatchJson);
            var root = document.RootElement;
            var effects = root.GetProperty("Effects");
            var stateSpaceId = root.GetProperty("StateSpaceId").GetString()!;
            var subjects = effects.EnumerateArray().Select(value => value.GetProperty("EntityId").GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            var procedures = root.GetProperty("ProceduresUsed").EnumerateArray()
                .Select(value => value.GetString() ?? throw new JsonException());
            var projection = root.GetProperty("ProjectionJson").GetString()!;
            return operation.Success && operation.Tool == ApplicationEcsExecutionIdentity.AuditTool
                && operation.Summary == $"Validated {effects.GetArrayLength()} application ECS effect(s) in '{stateSpaceId}'."
                && operation.Subject == string.Join(',', subjects)
                && operation.Intent == root.GetProperty("Intent").GetString()
                && operation.ProceduresCited == string.Join(',', procedures)
                && !operation.ConsumedReadEvidence
                && operation.MechanicId == definition.DefinitionId
                && operation.MechanicVersion == definition.Revision
                && operation.Seed == root.GetProperty("Seed").GetInt64()
                && operation.ProjectionJson == projection
                && DataFingerprint(projection) == sample.ProjectionFingerprint;
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException
            or ArgumentException or InvalidOperationException or NotSupportedException) { return false; }
    }

    private async Task<bool> IsPublishedRebindingAsync(StateSpaceView state, string retainedRevision,
        ActiveApplicationManifest predecessor, ActiveApplicationManifest successor,
        CancellationToken cancellationToken)
    {
        const string prefix = "state-binding.";
        var separator = retainedRevision.LastIndexOf('.');
        if (!retainedRevision.StartsWith(prefix, StringComparison.Ordinal)
            || separator <= prefix.Length
            || !int.TryParse(retainedRevision[prefix.Length..separator], out var retainedBindingRevision)
            || retainedRevision[(separator + 1)..] != predecessor.ActivationFingerprint.ToLowerInvariant()
            || state.ManifestFingerprint != successor.ActivationFingerprint
            || state.ResolutionFingerprint != successor.ResolutionFingerprint
            || state.BindingRevision != retainedBindingRevision + 1) return false;
        var history = await db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == state.StateSpaceId
                && (value.BindingRevision == retainedBindingRevision
                    || value.BindingRevision == state.BindingRevision))
            .OrderBy(value => value.BindingRevision).ToArrayAsync(cancellationToken);
        if (history.Length != 2) return false;
        var baseline = history[0];
        var current = history[1];
        return baseline.ActiveFingerprint == predecessor.ActivationFingerprint
            && baseline.ResolutionFingerprint == predecessor.ResolutionFingerprint
            && current.ActiveFingerprint == successor.ActivationFingerprint
            && current.ResolutionFingerprint == successor.ResolutionFingerprint
            && current.PreviousBindingFingerprint == baseline.BindingFingerprint
            && current.CompatibilityCode == "compatible-candidate-publication";
    }

    private async Task<StandingGrantRevision?> SelectGrantAsync(InteractionInvocationHost host, StateSpaceView state,
        StandingGrantDefinitionReference predecessor, CancellationToken cancellationToken)
    {
        var candidates = await grantCandidates.ReadAsync(host.Principal, host.ApplicationRevision.ApplicationId,
            StandingGrantScope.StateSpace, state.StateSpaceId,
            new HashSet<StandingGrantCapability> { StandingGrantCapability.Read, StandingGrantCapability.Execute }, cancellationToken);
        if (candidates.Status != StandingGrantReadCandidateStatus.Available) return null;
        foreach (var grant in candidates.Candidates.OrderBy(value => value.GrantReference, StringComparer.Ordinal))
        {
            if (grant.Revoked || grant.ExpiresAtUtc <= DateTime.UtcNow) continue;
            var stateHost = StateHost(host, state, grant.GrantReference);
            var resolved = await targets.ResolveAsync(stateHost, predecessor, cancellationToken);
            if (resolved is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }) continue;
            var read = await grants.EvaluateAsync(stateHost,
                new(StandingGrantCapability.Read, StandingGrantScope.StateSpace, [target], []), cancellationToken);
            if (read.Allowed && read.Grant?.GrantReference == grant.GrantReference
                && read.Grant.ContentFingerprint == grant.ContentFingerprint) return grant;
        }
        return null;
    }

    private async Task<StandingGrantDecision> EvaluateAuthorityAsync(InteractionInvocationHost host,
        StandingGrantRequirement requirement, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            return await grants.EvaluateAsync(host, requirement, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var decision = await grants.EvaluateAsync(host, requirement, cancellationToken);
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return decision;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static InteractionInvocationHost StateHost(InteractionInvocationHost source, StateSpaceView state,
        string grantReference) => new(source.Principal, source.ApplicationRevision, state.StateSpaceId,
        grantReference, source.CommandId, InteractionStateRevision.From(state),
        InteractionExecutionProfile.ReadOnly, source.Budget, source.ParentCommandId);

    private static bool SameRevision(ApplicationRevision left, ApplicationRevision right) =>
        left.ApplicationId == right.ApplicationId && left.Revision == right.Revision
        && left.Fingerprint == right.Fingerprint
        && left.BaseApplications.SequenceEqual(right.BaseApplications);

    private static IReadOnlyList<StandingGrantDefinitionReference>? Dependencies(
        ApplicationCandidateStatefulUpdateEvidence update, ICatalogNavigator catalog)
    {
        try
        {
            var result = new SortedDictionary<string, StandingGrantDefinitionReference>(StringComparer.Ordinal)
            { [update.PredecessorDefinition.DefinitionId] = update.PredecessorDefinition };
            var visits = 0;
            Walk(update.Requirements, update.Definition.DefinitionId, 0, new HashSet<string>(StringComparer.Ordinal));
            return result.Values.ToArray();

            void Walk(MechanicRequirements requirements, string current, int depth, HashSet<string> lineage)
            {
                if (depth >= 8 || !lineage.Add(current)) throw new InvalidOperationException();
                foreach (var child in requirements.Children.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    if (++visits > 256) throw new InvalidOperationException();
                    var id = child.Value.MechanicId.StartsWith(update.Candidate.ApplicationId.Value + ".", StringComparison.Ordinal)
                        ? child.Value.MechanicId : update.Candidate.ApplicationId.Value + "." + child.Value.MechanicId;
                    var record = catalog.Inspect(new(update.Candidate.ApplicationId,
                        update.Candidate.ApplicationId.Value, id));
                    if (record.Summary.Kind != "mechanic" || record.Summary.Status != "active"
                        || child.Value.MechanicVersion > 0 && (child.Value.MechanicVersion != record.Summary.Version
                            || child.Value.ContentFingerprint != record.Summary.ContentFingerprint))
                        throw new InvalidOperationException();
                    result[id] = new(id, "mechanic", record.Summary.Version, record.Summary.ContentFingerprint);
                    using var content = JsonDocument.Parse(record.ContentJson);
                    using var requirementsDocument = JsonDocument.Parse(
                        content.RootElement.GetProperty("requirements").GetString()!);
                    if (requirementsDocument.RootElement.EnumerateObject().Any(value =>
                            value.Name.Equals("service", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException();
                    var nested = MechanicRequirements.Parse(requirementsDocument.RootElement.GetRawText());
                    if (nested.Event is not null) throw new InvalidOperationException();
                    Walk(nested, id, depth + 1, new HashSet<string>(lineage, StringComparer.Ordinal));
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or KeyNotFoundException) { return null; }
    }

    internal static string DataFingerprint(string json) => InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/stateful-candidate-runtime-value/v1", InteractionCanonicalJson.Canonicalize(json));

    private sealed class CandidateCatalogOverlay(ICatalogNavigator active, CatalogRecordView candidate) : ICatalogNavigator
    {
        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) => active.ListCollections(applicationId);
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => active.Browse(request);
        public CatalogSearchResult Search(CatalogSearchRequest request) => active.Search(request);
        public CatalogRecordView Inspect(CatalogRecordRequest request) =>
            request.ApplicationId == ApplicationIdentifier.Parse(candidate.Summary.Collection)
            && request.QualifiedId == candidate.Summary.QualifiedId ? candidate : active.Inspect(request);
        public EffectiveApplicationContentResult EffectiveContent(EffectiveApplicationContentRequest request) => active.EffectiveContent(request);
        public ReadableRulesResult ReadableRules(ReadableRulesRequest request) => active.ReadableRules(request);
    }
}
