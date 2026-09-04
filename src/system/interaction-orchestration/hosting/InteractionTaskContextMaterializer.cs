using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Play;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

/// <summary>
/// Builds a small, host-bound planning snapshot. Retrieval may rank candidates with vectors, but
/// this materializer only emits records rehydrated from the current canonical catalog and state.
/// </summary>
public sealed class InteractionTaskContextMaterializer(
    IInteractionAuthorizationPolicy authorization,
    IInteractionFeatureRetriever retrieval,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IApplicationReadModelService readModels,
    IAuthorizedKnowledgeCandidateResolver? knowledge = null,
    IApplicationPlayRecordStore? play = null,
    IInteractionRecentReceiptReader? receipts = null) : IInteractionTaskContextMaterializer
{
    public const int MaximumPackBytes = 32 * 1024;
    public const int MaximumPackElapsedMilliseconds = 5_000;
    public const int MaximumPackItems = 64;
    private const int MaximumCapabilities = 12;
    private const int MaximumReadViews = 4;
    private const int MaximumKnowledge = 8;
    private const int MaximumFacts = 16;
    private const int MaximumReceipts = 6;

    public async Task<InteractionTaskContextPack> MaterializeAsync(
        AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(MaximumPackElapsedMilliseconds);
        try
        {
            return await MaterializeCoreAsync(envelope, authorizationRequest, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                 && deadline.IsCancellationRequested)
        {
            throw Failure("TASK_CONTEXT_TIME_BUDGET_EXCEEDED",
                "Task-context materialization exceeded its closed time budget.");
        }
    }

    private async Task<InteractionTaskContextPack> MaterializeCoreAsync(
        AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(authorizationRequest);
        EnsureRequestScope(envelope, authorizationRequest);
        var before = Authorize(authorizationRequest);

        var snapshot = CurrentSnapshot(envelope);
        var search = await retrieval.SearchAsync(
            new(envelope.Host.ApplicationRevision.ApplicationId, InteractionRetrievalLane.TrustedFeature),
            new(envelope.Intent.IntentText, MaximumCapabilities,
                ["mechanic", ApplicationQueryContract.CatalogKind], ["active"]),
            cancellationToken);
        if (search.Mode == InteractionRetrievalMode.Unavailable)
            throw Failure("TASK_CONTEXT_RETRIEVAL_UNAVAILABLE",
                "Current capability retrieval is unavailable.");

        var records = Rehydrate(snapshot, search.Hits);
        var capabilityItems = records.Select(record =>
        {
            var descriptor = ApplicationCapabilityContractAdapter.Create(
                envelope.Host.ApplicationRevision.ApplicationId, record, envelope.Host.StateSpaceId);
            return Item($"capability:{descriptor.Id}@{descriptor.Version}#{descriptor.Fingerprint}",
                descriptor.Version.ToString(), descriptor.Fingerprint, descriptor);
        }).ToList();

        var limitations = new List<string>();
        var omissions = new List<PackOmission>();
        if (!string.IsNullOrEmpty(search.AvailabilityCode))
            limitations.Add(search.AvailabilityCode);
        var knowledgeItems = await ReadKnowledge(envelope, limitations, omissions, cancellationToken);
        var readViewItems = await ReadViews(
            envelope, records, knowledgeItems.Audience, limitations, omissions, cancellationToken);
        var continuity = ReadContinuity(envelope, omissions);
        var receiptItems = await ReadReceipts(envelope, limitations, omissions, cancellationToken);

        EnsureContinuityUnchanged(envelope, continuity);
        EnsureSnapshotUnchanged(envelope, snapshot);
        var after = Authorize(authorizationRequest);
        if (after.EvidenceReference != before.EvidenceReference)
            throw Failure("TASK_CONTEXT_AUTHORIZATION_STALE",
                "Authorization changed while task context was materialized.");

        var scopeItems = ScopeItems(envelope, before, knowledgeItems.Audience);
        var document = new PackDocument(
            scopeItems,
            capabilityItems,
            readViewItems,
            knowledgeItems.Items,
            continuity.Facts,
            continuity.Items,
            receiptItems,
            limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            new(MaximumPackBytes, MaximumPackItems, MaximumPackElapsedMilliseconds,
                MaximumCapabilities, MaximumReadViews, MaximumKnowledge, MaximumFacts,
                MaximumReceipts), omissions);
        var json = Fit(document);
        var references = document.AllItems().Select(value => value.Reference)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var fingerprint = Hash(json);
        return new(InteractionTaskContextProfiles.Version2, json, fingerprint,
            Array.AsReadOnly(references));
    }

    private async Task<List<ContextItem>> ReadViews(
        AuthorizedInteractionEnvelope envelope,
        IReadOnlyList<CatalogRecordDefinition> records,
        AudienceContext? audience,
        List<string> limitations,
        List<PackOmission> omissions,
        CancellationToken cancellationToken)
    {
        var values = new List<ContextItem>();
        foreach (var record in records.Where(value => value.Kind == ApplicationQueryContract.CatalogKind))
        {
            ApplicationQueryContract contract;
            try { contract = ApplicationQueryContract.Parse(record.ContentJson, envelope.Host.ApplicationRevision.ApplicationId); }
            catch (Exception exception) when (exception is ArgumentException or JsonException)
            {
                limitations.Add("TASK_CONTEXT_QUERY_CONTRACT_INVALID");
                continue;
            }
            if (contract.Exposure != ApplicationQueryExposure.ModelVisible
                || contract.Roles.Keys.Any(role => !envelope.Intent.RoleHints.ContainsKey(role)))
                continue;
            if (values.Count >= MaximumReadViews)
            {
                RecordOmission(omissions, "readViews", "item-budget");
                continue;
            }
            var bindings = contract.Roles.Keys.Order(StringComparer.Ordinal)
                .ToDictionary(role => role, role => envelope.Intent.RoleHints[role], StringComparer.Ordinal);
            try
            {
                var result = await readModels.ReadAsync(new(envelope.Host.StateSpaceId,
                    envelope.Host.ApplicationRevision.ApplicationId, contract.Id, bindings,
                    audience is null ? null : audience.ActorAudience
                        ? MechanicAudienceContext.Player
                        : MechanicAudienceContext.GameMaster), cancellationToken);
                if (result.StateSpaceFingerprint != envelope.Host.EffectiveSetFingerprint
                    || result.ResolutionFingerprint != envelope.Host.ResolutionFingerprint)
                    throw Failure("TASK_CONTEXT_READ_VIEW_STALE",
                        "A read view does not match the authorized state-space revision.");
                values.Add(Item($"read-view:{result.QualifiedQueryId}#{result.ResultFingerprint}",
                    result.SourceRevisionFingerprint, result.ResultFingerprint, new
                    {
                        result.ApplicationId,
                        result.StateSpaceId,
                        result.QualifiedQueryId,
                        result.OutputSchemaHash,
                        result.SourceRevisionFingerprint,
                        data = JsonSerializer.Deserialize<JsonElement>(result.DataJson)
                    }));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (InteractionTaskContextException) { throw; }
            catch (ApplicationReadModelException exception)
            {
                limitations.Add(exception.Code);
            }
        }
        return values;
    }

    private async Task<KnowledgeContext> ReadKnowledge(
        AuthorizedInteractionEnvelope envelope,
        List<string> limitations,
        List<PackOmission> omissions,
        CancellationToken cancellationToken)
    {
        if (knowledge is null || !envelope.Intent.RoleHints.TryGetValue("campaign", out var campaignId))
            return new([], null);
        var result = await knowledge.ResolveAsync(new(campaignId, envelope.Intent.IntentText,
            CandidateLimit: MaximumKnowledge), cancellationToken);
        if (!result.Granted)
        {
            limitations.Add(string.IsNullOrEmpty(result.ErrorCode)
                ? "KNOWLEDGE_AUDIENCE_DENIED" : result.ErrorCode);
            return new([], null);
        }
        if (!string.IsNullOrEmpty(result.ErrorCode)) limitations.Add(result.ErrorCode);
        if (result.Candidates.Count > MaximumKnowledge)
            RecordOmission(omissions, "knowledge", "item-budget",
                result.Candidates.Count - MaximumKnowledge);
        var items = result.Candidates.Take(MaximumKnowledge).Select(candidate =>
            Item($"knowledge:{candidate.KnowledgeId}#{candidate.Revision}", candidate.Revision,
                candidate.Revision, new
                {
                    candidate.KnowledgeId,
                    candidate.Text,
                    candidate.Stance,
                    candidate.PresentationKind,
                    result.ScopeRevision
                })).ToList();
        return new(items, new AudienceContext(result.ActorAudience, result.PolicyRevision,
            result.ScopeRevision));
    }

    private ContinuityContext ReadContinuity(
        AuthorizedInteractionEnvelope envelope,
        List<PackOmission> omissions)
    {
        if (play is null) return new([], [], null, null);
        var conversation = play.GetSession(new(
            envelope.Host.Principal.PrincipalId,
            envelope.Host.ApplicationRevision.ApplicationId.Value,
            envelope.Host.StateSpaceId,
            envelope.Host.SessionContextId));
        if (conversation is null) return new([], [], null, null);
        if (conversation.PrincipalId != envelope.Host.Principal.PrincipalId
            || conversation.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId.Value
            || conversation.StateSpaceId != envelope.Host.StateSpaceId
            || conversation.SessionContextId != envelope.Host.SessionContextId)
            throw Failure("TASK_CONTEXT_CONTINUITY_SCOPE_MISMATCH",
                "Session continuity does not match the authorized scope.");

        var requested = envelope.Intent.ConversationFactReferences.ToHashSet(StringComparer.Ordinal);
        var eligibleFacts = conversation.KnownTruths
            .Where(value => requested.Count == 0 || requested.Contains(value.Id))
            .OrderByDescending(value => value.Ordinal).ToArray();
        if (eligibleFacts.Length > MaximumFacts)
            RecordOmission(omissions, "facts", "item-budget",
                eligibleFacts.Length - MaximumFacts);
        var facts = eligibleFacts.Take(MaximumFacts)
            .Select(value => Item($"fact:{value.Id}#{HashObject(value)}", value.Ordinal.ToString(),
                HashObject(value), value)).ToList();
        var continuity = new List<ContextItem>
        {
            Item($"continuity:{conversation.Id}@{conversation.Revision}#{HashObject(new
                {
                    conversation.Id, conversation.Revision, conversation.Status,
                    conversation.TotalMessageCount, conversation.CurrentSituation,
                    conversation.RecentMessages
                })}", conversation.Revision.ToString(), HashObject(new
                {
                    conversation.Id, conversation.Revision, conversation.Status,
                    conversation.TotalMessageCount, conversation.CurrentSituation,
                    conversation.RecentMessages
                }), new
                {
                    conversation.Id,
                    conversation.Status,
                    conversation.TotalMessageCount,
                    conversation.CurrentSituation,
                    conversation.RecentMessages
                })
        };
        return new(facts, continuity, conversation.Id, conversation.Revision);
    }

    private void EnsureContinuityUnchanged(
        AuthorizedInteractionEnvelope envelope,
        ContinuityContext before)
    {
        if (play is null || before.ConversationId is null) return;
        var after = play.GetSession(new(
            envelope.Host.Principal.PrincipalId,
            envelope.Host.ApplicationRevision.ApplicationId.Value,
            envelope.Host.StateSpaceId,
            envelope.Host.SessionContextId));
        if (after is null || after.Id != before.ConversationId || after.Revision != before.Revision)
            throw Failure("TASK_CONTEXT_CONTINUITY_STALE",
                "Session continuity changed while task context was materialized.");
    }

    private async Task<List<ContextItem>> ReadReceipts(
        AuthorizedInteractionEnvelope envelope,
        List<string> limitations,
        List<PackOmission> omissions,
        CancellationToken cancellationToken)
    {
        if (receipts is null) return [];
        try
        {
            var request = new InteractionAuthorizationRequest(envelope.Host.Principal,
                envelope.Host.ApplicationRevision.ApplicationId, envelope.Host.StateSpaceId,
                InteractionCapability.ReadReceipt, "interaction.task-context.receipts");
            var values = await receipts.ReadRecentAsync(request, envelope.Host.SessionContextId,
                MaximumReceipts, cancellationToken);
            var eligible = values.Where(value => value.SessionContextId == envelope.Host.SessionContextId
                    && value.ApplicationRevision == envelope.Host.ApplicationRevision.Revision
                    && value.ApplicationFingerprint == envelope.Host.ApplicationRevision.Fingerprint
                    && value.StateRevision == envelope.Host.StateRevision
                    && value.EffectiveSetFingerprint == envelope.Host.EffectiveSetFingerprint).ToArray();
            if (eligible.Length > MaximumReceipts)
                RecordOmission(omissions, "recentReceipts", "item-budget",
                    eligible.Length - MaximumReceipts);
            return eligible.Take(MaximumReceipts)
                .Select(value => Item(value.Reference, value.StateRevision,
                    value.Receipt.RequestFingerprint, new
                    {
                        value.SessionContextId,
                        value.ApplicationRevision,
                        value.ApplicationFingerprint,
                        value.StateRevision,
                        value.EffectiveSetFingerprint,
                        value.AuthorizationEvidenceReference,
                        receipt = value.Receipt
                    })).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            limitations.Add("TASK_CONTEXT_RECEIPTS_UNAVAILABLE");
            return [];
        }
    }

    private static IReadOnlyList<CatalogRecordDefinition> Rehydrate(
        ActiveCatalogFeatureSnapshot snapshot,
        IReadOnlyList<InteractionFeatureHit> hits)
    {
        var values = new List<CatalogRecordDefinition>();
        foreach (var hit in hits)
        {
            var document = snapshot.Documents.SingleOrDefault(value =>
                value.Trust == SourceTrust.Trusted
                && value.Record.Status == "active"
                && value.Record.Kind is "mechanic" or ApplicationQueryContract.CatalogKind
                && value.Record.QualifiedId == hit.Reference.QualifiedId
                && value.Record.Version == hit.Reference.Version
                && value.Record.ContentFingerprint == hit.Reference.ContentFingerprint
                && value.Record.ContentJson == hit.ContractJson);
            if (document is null || hit.Reference.CatalogFingerprint != snapshot.Manifest.Fingerprint)
                throw Failure("TASK_CONTEXT_CAPABILITY_STALE",
                    "A retrieved capability no longer matches the current canonical catalog.");
            values.Add(document.Record);
        }
        return values;
    }

    private ActiveCatalogFeatureSnapshot CurrentSnapshot(AuthorizedInteractionEnvelope envelope)
    {
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot))
            throw Failure("TASK_CONTEXT_CATALOG_UNAVAILABLE",
                "The current application catalog is unavailable.");
        EnsureSnapshot(envelope, snapshot);
        return snapshot;
    }

    private void EnsureSnapshotUnchanged(
        AuthorizedInteractionEnvelope envelope,
        ActiveCatalogFeatureSnapshot before)
    {
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var after)
            || after.Manifest.Fingerprint != before.Manifest.Fingerprint
            || (after.Resolution?.Fingerprint ?? after.Manifest.Fingerprint)
                != (before.Resolution?.Fingerprint ?? before.Manifest.Fingerprint))
            throw Failure("TASK_CONTEXT_CATALOG_STALE",
                "The application catalog changed while task context was materialized.");
        EnsureSnapshot(envelope, after);
    }

    private static void EnsureSnapshot(
        AuthorizedInteractionEnvelope envelope,
        ActiveCatalogFeatureSnapshot snapshot)
    {
        if (snapshot.Manifest.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId
            || snapshot.Manifest.Fingerprint != envelope.Host.EffectiveSetFingerprint
            || (snapshot.Resolution?.Fingerprint ?? snapshot.Manifest.Fingerprint)
                != envelope.Host.ResolutionFingerprint)
            throw Failure("TASK_CONTEXT_CATALOG_STALE",
                "The current catalog does not match the authorized task scope.");
    }

    private InteractionAuthorizationDecision Authorize(InteractionAuthorizationRequest request)
    {
        InteractionAuthorizationDecision decision;
        try { decision = authorization.Evaluate(request); }
        catch { throw Failure("TASK_CONTEXT_AUTHORIZATION_FAILED", "Task-context authorization failed closed."); }
        if (!decision.Allowed || decision.Capability != InteractionCapability.Plan
            || decision.PrincipalReference != request.Principal.PrincipalId
            || decision.ApplicationId != request.ApplicationId
            || decision.StateSpaceId != request.StateSpaceId)
            throw Failure("TASK_CONTEXT_NOT_AUTHORIZED",
                "Task context is not authorized for this scope.");
        return decision;
    }

    private static void EnsureRequestScope(
        AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationRequest request)
    {
        if (request.Capability != InteractionCapability.Plan
            || request.Principal.PrincipalId != envelope.Host.Principal.PrincipalId
            || request.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId
            || request.StateSpaceId != envelope.Host.StateSpaceId)
            throw Failure("TASK_CONTEXT_AUTHORIZATION_SCOPE_MISMATCH",
                "The task-context request does not match the authorized envelope.");
    }

    private static List<ContextItem> ScopeItems(
        AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationDecision authorization,
        AudienceContext? audience)
    {
        var app = envelope.Host.ApplicationRevision;
        var values = new List<ContextItem>
        {
            Item($"application:{app.ApplicationId.Value}@{app.Revision}#{app.Fingerprint}",
                app.Revision.ToString(), app.Fingerprint, new
                {
                    applicationId = app.ApplicationId.Value,
                    app.Revision,
                    app.BaseApplications
                }),
            Item($"state-space:{envelope.Host.StateSpaceId}@{envelope.Host.StateRevision}#{envelope.Host.EffectiveSetFingerprint}",
                envelope.Host.StateRevision, envelope.Host.EffectiveSetFingerprint, new
                {
                    envelope.Host.StateSpaceId,
                    envelope.Host.SessionContextId,
                    envelope.Host.ResolutionFingerprint
                }),
            Item($"principal:{envelope.Host.Principal.PrincipalId}#{HashObject(new
                {
                    envelope.Host.Principal.PrincipalId,
                    envelope.Host.Principal.AuthenticationMethod
                })}", envelope.Host.Principal.AuthenticationMethod, HashObject(new
                {
                    envelope.Host.Principal.PrincipalId,
                    envelope.Host.Principal.AuthenticationMethod
                }), new
                {
                    envelope.Host.Principal.PrincipalId,
                    envelope.Host.Principal.AuthenticationMethod
                }),
            Item($"audience:{authorization.EvidenceReference}#{HashObject(new
                {
                    authorization.EvidenceReference,
                    envelope.Host.RoleProfile.StableKey,
                    audience
                })}", audience?.PolicyRevision ?? authorization.Code, HashObject(new
                {
                    authorization.EvidenceReference,
                    envelope.Host.RoleProfile.StableKey,
                    audience
                }), new
                {
                    authorization.EvidenceReference,
                    roleProfile = envelope.Host.RoleProfile.StableKey,
                    knowledge = audience
                })
        };
        return values;
    }

    private static string Fit(PackDocument document)
    {
        while (true)
        {
            if (document.AllItems().Count() > MaximumPackItems)
                throw Failure("TASK_CONTEXT_ITEM_BUDGET_EXCEEDED",
                    "The mandatory task context exceeds the closed item budget.");
            var json = SerializeRaw(document);
            if (Encoding.UTF8.GetByteCount(json) <= MaximumPackBytes)
                return InteractionCanonicalJson.CanonicalizeObject(json);
            if (RemoveLast(document, document.Receipts, "recentReceipts")) continue;
            if (RemoveLast(document, document.Facts, "facts")) continue;
            if (RemoveLast(document, document.Knowledge, "knowledge")) continue;
            if (RemoveLast(document, document.Continuity, "continuity")) continue;
            if (RemoveLast(document, document.ReadViews, "readViews")) continue;
            if (document.Capabilities.Count > 1
                && RemoveLast(document, document.Capabilities, "capabilities"))
            {
                continue;
            }
            throw Failure("TASK_CONTEXT_BUDGET_EXCEEDED",
                "The mandatory task context exceeds the closed byte budget.");
        }
    }

    private static string SerializeRaw(PackDocument document) =>
        JsonSerializer.Serialize(new
        {
            profile = InteractionTaskContextProfiles.Version2,
            scope = document.Scope,
            capabilities = document.Capabilities,
            readViews = document.ReadViews,
            knowledge = document.Knowledge,
            facts = document.Facts,
            continuity = document.Continuity,
            recentReceipts = document.Receipts,
            limitations = document.Limitations,
            budgets = new
            {
                maximumBytes = document.Budgets.MaximumBytes,
                maximumItems = document.Budgets.MaximumItems,
                maximumElapsedMilliseconds = document.Budgets.MaximumElapsedMilliseconds,
                maximumCapabilities = document.Budgets.MaximumCapabilities,
                maximumReadViews = document.Budgets.MaximumReadViews,
                maximumKnowledge = document.Budgets.MaximumKnowledge,
                maximumFacts = document.Budgets.MaximumFacts,
                maximumReceipts = document.Budgets.MaximumReceipts
            },
            omissions = document.Omissions.OrderBy(value => value.Section, StringComparer.Ordinal)
                .ThenBy(value => value.Reason, StringComparer.Ordinal)
                .Select(value => new
                {
                    section = value.Section,
                    reason = value.Reason,
                    removedItems = value.RemovedItems
                })
        });

    private static bool RemoveLast(
        PackDocument document,
        List<ContextItem> values,
        string section)
    {
        if (values.Count == 0) return false;
        values.RemoveAt(values.Count - 1);
        RecordOmission(document.Omissions, section, "byte-budget");
        MarkTruncated(document);
        return true;
    }

    private static void RecordOmission(
        List<PackOmission> omissions,
        string section,
        string reason,
        int removedItems = 1)
    {
        var index = omissions.FindIndex(value => value.Section == section && value.Reason == reason);
        if (index < 0)
            omissions.Add(new(section, reason, removedItems));
        else
            omissions[index] = omissions[index] with
            {
                RemovedItems = omissions[index].RemovedItems + removedItems
            };
    }

    private static void MarkTruncated(PackDocument document)
    {
        if (!document.Limitations.Contains("TASK_CONTEXT_TRUNCATED", StringComparer.Ordinal))
        {
            document.Limitations.Add("TASK_CONTEXT_TRUNCATED");
            document.Limitations.Sort(StringComparer.Ordinal);
        }
    }

    private static ContextItem Item(string reference, string revision, string fingerprint, object value) =>
        new(reference, revision, fingerprint, JsonSerializer.SerializeToElement(value));

    private static string HashObject(object value) => Hash(
        InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(value)));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static InteractionTaskContextException Failure(string code, string message) => new(code, message);

    private sealed record ContextItem(
        string Reference,
        string Revision,
        string Fingerprint,
        JsonElement Value);

    private sealed record AudienceContext(
        bool ActorAudience,
        string PolicyRevision,
        string ScopeRevision);

    private sealed record KnowledgeContext(
        List<ContextItem> Items,
        AudienceContext? Audience);

    private sealed record ContinuityContext(
        List<ContextItem> Facts,
        List<ContextItem> Items,
        string? ConversationId,
        int? Revision);

    private sealed record PackDocument(
        List<ContextItem> Scope,
        List<ContextItem> Capabilities,
        List<ContextItem> ReadViews,
        List<ContextItem> Knowledge,
        List<ContextItem> Facts,
        List<ContextItem> Continuity,
        List<ContextItem> Receipts,
        List<string> Limitations,
        PackBudgets Budgets,
        List<PackOmission> Omissions)
    {
        public IEnumerable<ContextItem> AllItems() => Scope.Concat(Capabilities).Concat(ReadViews)
            .Concat(Knowledge).Concat(Facts).Concat(Continuity).Concat(Receipts);
    }

    private sealed record PackBudgets(
        int MaximumBytes,
        int MaximumItems,
        int MaximumElapsedMilliseconds,
        int MaximumCapabilities,
        int MaximumReadViews,
        int MaximumKnowledge,
        int MaximumFacts,
        int MaximumReceipts);

    private sealed record PackOmission(string Section, string Reason, int RemovedItems);
}
