using System.Text.Json;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Knowledge;

/// <summary>Exact campaign-participation graph check driven solely by application binding vocabulary.</summary>
public sealed class ApplicationKnowledgeActorParticipationVerifier(
    IEntityComponentStore entities,
    IStateSpaceEdgeStore edges) : IKnowledgeActorParticipationVerifier
{
    public async Task<KnowledgeParticipationResolution> ResolveAsync(
        KnowledgeApplicationBinding binding,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (binding is null || !Token(actorId)) return KnowledgeParticipationResolution.Denied();
        try
        {
            binding.Validate();
            var actor = await entities.GetEntityAsync(binding.StateSpaceId, actorId, cancellationToken);
            if (actor is null) return KnowledgeParticipationResolution.Denied();
            var relationships = await edges.ListRelationshipsAsync(binding.StateSpaceId, cancellationToken);
            var campaignLinks = relationships.Where(value =>
                value.FromEntityId == binding.CampaignEntityId &&
                value.QualifiedKind == binding.CampaignParticipationRelationshipKind &&
                Empty(value.DataJson)).ToArray();
            var matches = new List<(EcsEntityView Participation, EcsComponentView Status,
                EcsRelationshipView CampaignLink, EcsRelationshipView ActorLink)>();
            foreach (var campaignLink in campaignLinks)
            {
                var participation = await entities.GetEntityAsync(
                    binding.StateSpaceId, campaignLink.ToEntityId, cancellationToken);
                if (participation is null) continue;
                var owners = relationships.Where(value =>
                    value.ToEntityId == participation.EntityId &&
                    value.QualifiedKind == binding.CampaignParticipationRelationshipKind &&
                    Empty(value.DataJson)).ToArray();
                if (owners.Length != 1 || owners[0].FromEntityId != binding.CampaignEntityId)
                    continue;
                var status = await entities.GetComponentAsync(binding.StateSpaceId,
                    participation.EntityId, binding.ParticipationComponentTypeId, cancellationToken);
                if (status is null || !ExactText(status.ValueJson, binding.ParticipationStatusProperty,
                        binding.ActiveParticipationStatus)) continue;
                var actorLinks = relationships.Where(value =>
                    value.FromEntityId == participation.EntityId &&
                    value.QualifiedKind == binding.ParticipationActorRelationshipKind &&
                    Empty(value.DataJson)).ToArray();
                if (actorLinks.Length == 1 && actorLinks[0].ToEntityId == actorId)
                    matches.Add((participation, status, campaignLink, actorLinks[0]));
            }
            if (matches.Count != 1) return KnowledgeParticipationResolution.Denied();
            var match = matches[0];
            var revision = ApplicationKnowledgeCanonicalSource.Hash(new
            {
                binding.BindingRevision,
                Campaign = binding.CampaignEntityId,
                Actor = new { actor.EntityId, actor.Revision },
                Participation = new { match.Participation.EntityId, match.Participation.Revision },
                Status = new { match.Status.Type, match.Status.Revision, match.Status.ValueJson },
                CampaignLink = new { match.CampaignLink.FromEntityId, match.CampaignLink.ToEntityId,
                    match.CampaignLink.QualifiedKind, match.CampaignLink.Revision },
                ActorLink = new { match.ActorLink.FromEntityId, match.ActorLink.ToEntityId,
                    match.ActorLink.QualifiedKind, match.ActorLink.Revision }
            });
            return new(true, revision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return KnowledgeParticipationResolution.Denied();
        }
    }

    private static bool ExactText(string json, string property, string expected)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String && value.GetString() == expected;
        }
        catch (JsonException) { return false; }
    }

    private static bool Empty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException) { return false; }
    }

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() && value.Length <= 200 && !value.Any(char.IsWhiteSpace);
}
