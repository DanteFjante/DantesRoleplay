using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Knowledge;

/// <summary>
/// Binds one host-selected application to one exact current campaign using only its fingerprinted
/// active metadata and registered state spaces. Audience authorization is deliberately owned by
/// the caller and must run before this resolver.
/// </summary>
public sealed class ActivatedKnowledgeApplicationBindingResolver(
    KnowledgeApplicationSelection selection,
    IActivatedApplicationDocumentReader documents,
    IStateSpaceRegistry stateSpaces,
    IEntityComponentStore entities) : IKnowledgeApplicationBindingResolver
{
    private const int PageSize = 100;
    private const int MaximumStateSpaces = 1_000;

    public async Task<KnowledgeApplicationBinding?> ResolveAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        if (!Token(campaignId, 200)) return null;
        try
        {
            selection.Validate();
            var applicationId = ApplicationIdentifier.Parse(selection.ApplicationId);
            var document = documents.ReadText(applicationId, selection.BindingDocumentPath);
            if (document is null || document.ApplicationId != applicationId ||
                !KnowledgeApplicationBindingDocument.TryParse(document.Text, applicationId.Value, out var vocabulary))
                return null;

            var matches = new List<(StateSpaceView Space, EcsEntityView Campaign, EcsComponentView Root,
                KnowledgeApplicationBindingDocument.BindingDto Vocabulary)>();
            string? cursor = null;
            var seen = 0;
            do
            {
                var page = stateSpaces.ListPage(applicationId, cursor, PageSize);
                seen += page.StateSpaces.Count;
                if (seen > MaximumStateSpaces) return null;
                foreach (var stateSpace in page.StateSpaces)
                {
                    if (stateSpace.ApplicationRevision.ApplicationId != applicationId) return null;
                    var campaign = await entities.GetEntityAsync(
                        stateSpace.StateSpaceId, campaignId, cancellationToken);
                    if (campaign is null) continue;
                    var effectiveVocabulary = vocabulary;
                    var root = await entities.GetComponentAsync(stateSpace.StateSpaceId, campaignId,
                        vocabulary.CampaignRootComponentTypeId, cancellationToken);
                    if (root is null)
                    {
                        var legacyRootTypeId = LegacyApplicationIdentity(
                            applicationId.Value, vocabulary.CampaignRootComponentTypeId);
                        if (legacyRootTypeId != vocabulary.CampaignRootComponentTypeId)
                        {
                            root = await entities.GetComponentAsync(stateSpace.StateSpaceId, campaignId,
                                legacyRootTypeId, cancellationToken);
                            if (root is not null)
                                effectiveVocabulary = vocabulary.WithApplicationPrefix(applicationId.Value);
                        }
                    }
                    if (root is not null && ExactText(root.ValueJson, vocabulary.CampaignStatusProperty,
                            vocabulary.ActiveCampaignStatus))
                        matches.Add((stateSpace, campaign, root, effectiveVocabulary));
                }
                cursor = page.NextStateSpaceId;
            } while (cursor is not null);

            if (matches.Count != 1) return null;
            var match = matches[0];
            var revision = Hash(JsonSerializer.SerializeToUtf8Bytes(new
            {
                document.ActivationRevision,
                document.ActivationFingerprint,
                document.ContentFingerprint,
                match.Space.StateSpaceId,
                ApplicationRevision = match.Space.ApplicationRevision.Revision,
                match.Space.ManifestFingerprint,
                StateSpaceBindingRevision = match.Space.BindingRevision,
                CampaignRevision = match.Campaign.Revision,
                RootType = match.Root.Type,
                RootRevision = match.Root.Revision
            }));
            var binding = match.Vocabulary.Bind(
                applicationId.Value, match.Space.StateSpaceId, campaignId, revision);
            binding.Validate();
            return binding;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
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

    private static bool Token(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum &&
        !value.Any(char.IsWhiteSpace);
    private static string LegacyApplicationIdentity(string applicationId, string qualifiedId) =>
        qualifiedId.StartsWith(applicationId + ".", StringComparison.Ordinal)
            ? qualifiedId
            : $"{applicationId}.{qualifiedId}";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

internal static class KnowledgeApplicationBindingDocument
{
    private const string CurrentFormat = "system.knowledge.binding.v1";
    private const int MaximumDocumentLength = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static bool TryParse(string text, string expectedApplicationId, out BindingDto binding)
    {
        binding = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumDocumentLength) return false;
        try
        {
            var document = JsonSerializer.Deserialize<DocumentDto>(text, Json);
            if (document is null || document.Format != CurrentFormat ||
                document.ApplicationId != expectedApplicationId || document.Binding is null)
                return false;
            var candidate = document.Binding.Bind(expectedApplicationId, "state-space", "campaign", "revision");
            candidate.Validate();
            binding = document.Binding;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record DocumentDto
    {
        public string Format { get; init; } = "";
        public string ApplicationId { get; init; } = "";
        public BindingDto? Binding { get; init; }
    }

    internal sealed record BindingDto
    {
        public string CampaignRootComponentTypeId { get; init; } = "";
        public string CampaignStatusProperty { get; init; } = "";
        public string ActiveCampaignStatus { get; init; } = "";
        public string CampaignWorldRelationshipKind { get; init; } = "";
        public string ParticipationComponentTypeId { get; init; } = "";
        public string ParticipationStatusProperty { get; init; } = "";
        public string ActiveParticipationStatus { get; init; } = "";
        public string CampaignParticipationRelationshipKind { get; init; } = "";
        public string ParticipationActorRelationshipKind { get; init; } = "";
        public string WorldRootComponentTypeId { get; init; } = "";
        public string WorldStatusProperty { get; init; } = "";
        public string ActiveWorldStatus { get; init; } = "";
        public string WorldClockComponentTypeId { get; init; } = "";
        public string CurrentMinuteProperty { get; init; } = "";
        public IReadOnlyList<KnowledgeKindBinding> KnowledgeKinds { get; init; } = [];
        public string PrimaryStatusProperty { get; init; } = "";
        public string PrimarySummaryProperty { get; init; } = "";
        public string ClassificationComponentTypeId { get; init; } = "";
        public string ClassificationSensitivityProperty { get; init; } = "";
        public string ValidityComponentTypeId { get; init; } = "";
        public string ValidFromProperty { get; init; } = "";
        public string ValidUntilProperty { get; init; } = "";
        public string KnowledgeWorldRelationshipKind { get; init; } = "";
        public string KnowledgeAboutRelationshipKind { get; init; } = "";
        public string ExplicitStateRelationshipKind { get; init; } = "";
        public string BaselineRelationshipKind { get; init; } = "";
        public string StateProperty { get; init; } = "";
        public string BaselineInheritanceProperty { get; init; } = "";
        public string BaselineInheritanceValue { get; init; } = "";
        public IReadOnlyList<string> ContentStates { get; init; } = [];
        public string FamiliarState { get; init; } = "";
        public string UnknownState { get; init; } = "";
        public string BaselineState { get; init; } = "";
        public string FactionComponentTypeId { get; init; } = "";
        public string FactionStatusProperty { get; init; } = "";
        public string ActiveFactionStatus { get; init; } = "";
        public string FactionWorldRelationshipKind { get; init; } = "";
        public string FactionMemberRelationshipKind { get; init; } = "";
        public string LocationComponentTypeId { get; init; } = "";
        public string LocationStatusProperty { get; init; } = "";
        public string ActiveLocationStatus { get; init; } = "";
        public string LocationKindProperty { get; init; } = "";
        public string RegionLocationKind { get; init; } = "";

        internal BindingDto WithApplicationPrefix(string applicationId)
        {
            string Prefix(string value) => value.StartsWith(applicationId + ".", StringComparison.Ordinal)
                ? value
                : $"{applicationId}.{value}";
            return this with
            {
                CampaignRootComponentTypeId = Prefix(CampaignRootComponentTypeId),
                CampaignWorldRelationshipKind = Prefix(CampaignWorldRelationshipKind),
                ParticipationComponentTypeId = Prefix(ParticipationComponentTypeId),
                CampaignParticipationRelationshipKind = Prefix(CampaignParticipationRelationshipKind),
                ParticipationActorRelationshipKind = Prefix(ParticipationActorRelationshipKind),
                WorldRootComponentTypeId = Prefix(WorldRootComponentTypeId),
                WorldClockComponentTypeId = Prefix(WorldClockComponentTypeId),
                KnowledgeKinds = KnowledgeKinds.Select(value => value with
                {
                    ComponentTypeId = Prefix(value.ComponentTypeId)
                }).ToArray(),
                ClassificationComponentTypeId = Prefix(ClassificationComponentTypeId),
                ValidityComponentTypeId = Prefix(ValidityComponentTypeId),
                KnowledgeWorldRelationshipKind = Prefix(KnowledgeWorldRelationshipKind),
                KnowledgeAboutRelationshipKind = Prefix(KnowledgeAboutRelationshipKind),
                ExplicitStateRelationshipKind = Prefix(ExplicitStateRelationshipKind),
                BaselineRelationshipKind = Prefix(BaselineRelationshipKind),
                FactionComponentTypeId = Prefix(FactionComponentTypeId),
                FactionWorldRelationshipKind = Prefix(FactionWorldRelationshipKind),
                FactionMemberRelationshipKind = Prefix(FactionMemberRelationshipKind),
                LocationComponentTypeId = Prefix(LocationComponentTypeId)
            };
        }

        internal KnowledgeApplicationBinding Bind(
            string applicationId,
            string stateSpaceId,
            string campaignId,
            string revision) => new()
        {
            ApplicationId = applicationId,
            StateSpaceId = stateSpaceId,
            CampaignEntityId = campaignId,
            BindingRevision = revision,
            CampaignRootComponentTypeId = CampaignRootComponentTypeId,
            CampaignStatusProperty = CampaignStatusProperty,
            ActiveCampaignStatus = ActiveCampaignStatus,
            CampaignWorldRelationshipKind = CampaignWorldRelationshipKind,
            ParticipationComponentTypeId = ParticipationComponentTypeId,
            ParticipationStatusProperty = ParticipationStatusProperty,
            ActiveParticipationStatus = ActiveParticipationStatus,
            CampaignParticipationRelationshipKind = CampaignParticipationRelationshipKind,
            ParticipationActorRelationshipKind = ParticipationActorRelationshipKind,
            WorldRootComponentTypeId = WorldRootComponentTypeId,
            WorldStatusProperty = WorldStatusProperty,
            ActiveWorldStatus = ActiveWorldStatus,
            WorldClockComponentTypeId = WorldClockComponentTypeId,
            CurrentMinuteProperty = CurrentMinuteProperty,
            KnowledgeKinds = KnowledgeKinds,
            PrimaryStatusProperty = PrimaryStatusProperty,
            PrimarySummaryProperty = PrimarySummaryProperty,
            ClassificationComponentTypeId = ClassificationComponentTypeId,
            ClassificationSensitivityProperty = ClassificationSensitivityProperty,
            ValidityComponentTypeId = ValidityComponentTypeId,
            ValidFromProperty = ValidFromProperty,
            ValidUntilProperty = ValidUntilProperty,
            KnowledgeWorldRelationshipKind = KnowledgeWorldRelationshipKind,
            KnowledgeAboutRelationshipKind = KnowledgeAboutRelationshipKind,
            ExplicitStateRelationshipKind = ExplicitStateRelationshipKind,
            BaselineRelationshipKind = BaselineRelationshipKind,
            StateProperty = StateProperty,
            BaselineInheritanceProperty = BaselineInheritanceProperty,
            BaselineInheritanceValue = BaselineInheritanceValue,
            ContentStates = ContentStates,
            FamiliarState = FamiliarState,
            UnknownState = UnknownState,
            BaselineState = BaselineState,
            FactionComponentTypeId = FactionComponentTypeId,
            FactionStatusProperty = FactionStatusProperty,
            ActiveFactionStatus = ActiveFactionStatus,
            FactionWorldRelationshipKind = FactionWorldRelationshipKind,
            FactionMemberRelationshipKind = FactionMemberRelationshipKind,
            LocationComponentTypeId = LocationComponentTypeId,
            LocationStatusProperty = LocationStatusProperty,
            ActiveLocationStatus = ActiveLocationStatus,
            LocationKindProperty = LocationKindProperty,
            RegionLocationKind = RegionLocationKind
        };
    }
}
