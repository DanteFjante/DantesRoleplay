using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;

namespace DantesRoleplay.Knowledge;

public sealed class ReviewedKnowledgeStateSynchronizer(
    IAuthorizedKnowledgeAudiencePolicy audience,
    IKnowledgeApplicationBindingResolver bindings,
    IKnowledgeActorParticipationVerifier participation,
    IKnowledgeCanonicalSource source,
    IStateSpaceEdgeStore edges,
    IApplicationEcsEffectApplier effects) : IReviewedKnowledgeStateSynchronizer
{
    public async Task<ReviewedKnowledgeStateSyncResult> SynchronizeAsync(
        ReviewedKnowledgeStateSyncRequest request,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(request)) return Rejected(dryRun, "INVALID_KNOWLEDGE_SYNC");

        KnowledgeAudienceResolution resolvedAudience;
        try { resolvedAudience = await audience.ResolveAsync(request.CampaignId, cancellationToken); }
        catch { return Rejected(dryRun, "KNOWLEDGE_AUDIENCE_DENIED"); }
        if (!resolvedAudience.Granted || resolvedAudience.Grant!.CampaignId != request.CampaignId ||
            resolvedAudience.Grant.Role != KnowledgeAudienceRole.Actor)
            return Rejected(dryRun, "KNOWLEDGE_AUDIENCE_DENIED");

        var grant = resolvedAudience.Grant;
        try
        {
            var binding = await bindings.ResolveAsync(request.CampaignId, cancellationToken);
            if (binding is null || binding.CampaignEntityId != request.CampaignId)
                return Rejected(dryRun, "KNOWLEDGE_AUDIENCE_DENIED");
            binding.Validate();
            var active = await participation.ResolveAsync(binding, grant.ActorId!, cancellationToken);
            if (!active.Active || string.IsNullOrWhiteSpace(active.Revision))
                return Rejected(dryRun, "KNOWLEDGE_AUDIENCE_DENIED");

            var allowedStates = binding.ContentStates.Concat([binding.FamiliarState, binding.UnknownState])
                .ToHashSet(StringComparer.Ordinal);
            if (request.Entries.Any(value => !allowedStates.Contains(value.State)))
                return Rejected(dryRun, "INVALID_KNOWLEDGE_STATE");

            var scope = await source.ReadCampaignScopeAsync(binding, cancellationToken);
            if (scope is null || scope.CampaignId != request.CampaignId)
                return Rejected(dryRun, "KNOWLEDGE_SCOPE_UNAVAILABLE");
            var projection = await source.ReadWorldAsync(binding, scope, cancellationToken);
            if (projection is null || projection.Scope != scope)
                return Rejected(dryRun, "KNOWLEDGE_SCOPE_UNAVAILABLE");
            var canonical = projection.Documents.ToDictionary(value => value.KnowledgeId,
                StringComparer.Ordinal);
            if (request.Entries.Any(value => !canonical.ContainsKey(value.KnowledgeId)))
                return Rejected(dryRun, "KNOWLEDGE_RECORD_UNKNOWN");

            foreach (var entry in request.Entries)
            {
                var hydrated = await source.ReadDocumentAsync(
                    binding, scope.WorldId, entry.KnowledgeId, cancellationToken);
                if (hydrated is null || hydrated.Revision != canonical[entry.KnowledgeId].Revision)
                    return Rejected(dryRun, "KNOWLEDGE_INPUT_STALE");
            }

            var current = (await edges.ListRelationshipsAsync(binding.StateSpaceId, cancellationToken))
                .Where(value => value.FromEntityId == grant.ActorId &&
                    value.QualifiedKind == binding.ExplicitStateRelationshipKind)
                .ToDictionary(value => value.ToEntityId, StringComparer.Ordinal);
            var writes = new List<ApplicationEcsEffect>();
            foreach (var entry in request.Entries.OrderBy(value => value.KnowledgeId, StringComparer.Ordinal))
            {
                var data = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    [binding.StateProperty] = entry.State
                });
                current.TryGetValue(entry.KnowledgeId, out var existing);
                if (existing is not null && JsonEquivalent(existing.DataJson, data)) continue;
                writes.Add(new()
                {
                    Type = ApplicationEcsEffectType.RelationshipSet,
                    EntityId = grant.ActorId!,
                    TargetEntityId = entry.KnowledgeId,
                    QualifiedRelationshipKind = binding.ExplicitStateRelationshipKind,
                    DataJson = data,
                    ExpectedRevision = existing?.Revision ?? 0
                });
            }

            var fingerprint = Fingerprint(new
            {
                Version = "reviewed-knowledge-state-sync-v1",
                request.RequestToken,
                request.CampaignId,
                ActorId = grant.ActorId,
                binding.BindingRevision,
                scope.Revision,
                ParticipationRevision = active.Revision,
                DryRun = dryRun,
                Entries = request.Entries.OrderBy(value => value.KnowledgeId, StringComparer.Ordinal)
            });
            var batch = new ApplicationEcsEffectBatch
            {
                StateSpaceId = binding.StateSpaceId,
                Effects = writes.AsReadOnly(),
                Intent = "Synchronize an explicitly reviewed actor knowledge-state manifest.",
                ProceduresUsed = ["procedure.game.core.world.knowledge"],
                ExecutionIdentity = new(fingerprint[..32].ToLowerInvariant(), fingerprint)
            };
            var applied = await effects.ApplyAsync(batch, dryRun, cancellationToken);
            return new(applied.Valid, dryRun, applied.Replayed, request.Entries.Count, writes.Count,
                applied.OperationId, applied.Valid ? "" : applied.Problems.FirstOrDefault()?.Code ?? "KNOWLEDGE_SYNC_FAILED",
                applied.Problems);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Rejected(dryRun, "KNOWLEDGE_SYNC_FAILED");
        }
    }

    private static bool Valid(ReviewedKnowledgeStateSyncRequest? request) => request is not null &&
        Token(request.RequestToken, 200) && Token(request.CampaignId, 200) &&
        request.Entries is { Count: >= 1 and <= ApplicationEcsEffectValidation.MaximumEffects } &&
        request.Entries.All(value => value is not null && Token(value.KnowledgeId, 200) && Token(value.State, 100)) &&
        request.Entries.Select(value => value.KnowledgeId).Distinct(StringComparer.Ordinal).Count() == request.Entries.Count;

    private static bool Token(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum &&
        !value.Any(char.IsWhiteSpace);

    private static bool JsonEquivalent(string left, string right)
    {
        try
        {
            using var a = JsonDocument.Parse(left);
            using var b = JsonDocument.Parse(right);
            return JsonSerializer.Serialize(a.RootElement) == JsonSerializer.Serialize(b.RootElement);
        }
        catch (JsonException) { return false; }
    }

    private static string Fingerprint(object value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static ReviewedKnowledgeStateSyncResult Rejected(bool dryRun, string code) =>
        new(false, dryRun, false, 0, 0, "", code);
}
