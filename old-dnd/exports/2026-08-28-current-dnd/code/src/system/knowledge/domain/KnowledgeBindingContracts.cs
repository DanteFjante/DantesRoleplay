namespace DantesRoleplay.Knowledge;

public sealed record KnowledgeKindBinding(
    string ComponentTypeId,
    string Kind,
    string PresentationKind,
    IReadOnlyList<string> ArchivedStatuses);

/// <summary>
/// Exact application-owned vocabulary needed by the generic knowledge reader. Catalog/application
/// adapters supply this object; the generic kernel never embeds a ruleset's component or edge IDs.
/// </summary>
public sealed record KnowledgeApplicationBinding
{
    public required string ApplicationId { get; init; }
    public required string StateSpaceId { get; init; }
    public required string CampaignEntityId { get; init; }
    public required string BindingRevision { get; init; }

    public required string CampaignRootComponentTypeId { get; init; }
    public required string CampaignStatusProperty { get; init; }
    public required string ActiveCampaignStatus { get; init; }
    public required string CampaignWorldRelationshipKind { get; init; }
    public required string ParticipationComponentTypeId { get; init; }
    public required string ParticipationStatusProperty { get; init; }
    public required string ActiveParticipationStatus { get; init; }
    public required string CampaignParticipationRelationshipKind { get; init; }
    public required string ParticipationActorRelationshipKind { get; init; }

    public required string WorldRootComponentTypeId { get; init; }
    public required string WorldStatusProperty { get; init; }
    public required string ActiveWorldStatus { get; init; }
    public required string WorldClockComponentTypeId { get; init; }
    public required string CurrentMinuteProperty { get; init; }

    public required IReadOnlyList<KnowledgeKindBinding> KnowledgeKinds { get; init; }
    public required string PrimaryStatusProperty { get; init; }
    public required string PrimarySummaryProperty { get; init; }
    public required string ClassificationComponentTypeId { get; init; }
    public required string ClassificationSensitivityProperty { get; init; }
    public required string ValidityComponentTypeId { get; init; }
    public required string ValidFromProperty { get; init; }
    public required string ValidUntilProperty { get; init; }
    public required string KnowledgeWorldRelationshipKind { get; init; }
    public required string KnowledgeAboutRelationshipKind { get; init; }

    public required string ExplicitStateRelationshipKind { get; init; }
    public required string BaselineRelationshipKind { get; init; }
    public required string StateProperty { get; init; }
    public required string BaselineInheritanceProperty { get; init; }
    public required string BaselineInheritanceValue { get; init; }
    public required IReadOnlyList<string> ContentStates { get; init; }
    public required string FamiliarState { get; init; }
    public required string UnknownState { get; init; }
    public required string BaselineState { get; init; }

    public required string FactionComponentTypeId { get; init; }
    public required string FactionStatusProperty { get; init; }
    public required string ActiveFactionStatus { get; init; }
    public required string FactionWorldRelationshipKind { get; init; }
    public required string FactionMemberRelationshipKind { get; init; }

    public required string LocationComponentTypeId { get; init; }
    public required string LocationStatusProperty { get; init; }
    public required string ActiveLocationStatus { get; init; }
    public required string LocationKindProperty { get; init; }
    public required string RegionLocationKind { get; init; }

    public void Validate()
    {
        var ids = new[]
        {
            ApplicationId, StateSpaceId, CampaignEntityId, BindingRevision,
            CampaignRootComponentTypeId, CampaignWorldRelationshipKind,
            ParticipationComponentTypeId, CampaignParticipationRelationshipKind,
            ParticipationActorRelationshipKind, WorldRootComponentTypeId, WorldClockComponentTypeId,
            ClassificationComponentTypeId, ValidityComponentTypeId, KnowledgeWorldRelationshipKind,
            KnowledgeAboutRelationshipKind, ExplicitStateRelationshipKind, BaselineRelationshipKind,
            FactionComponentTypeId, FactionWorldRelationshipKind, FactionMemberRelationshipKind,
            LocationComponentTypeId
        };
        if (ids.Any(value => !Token(value, 200)))
            throw new ArgumentException("A knowledge binding requires bounded exact identities.");

        var fieldsAndValues = new[]
        {
            CampaignStatusProperty, ActiveCampaignStatus, ParticipationStatusProperty,
            ActiveParticipationStatus, WorldStatusProperty, ActiveWorldStatus,
            CurrentMinuteProperty, PrimaryStatusProperty, PrimarySummaryProperty,
            ClassificationSensitivityProperty, ValidFromProperty, ValidUntilProperty, StateProperty,
            BaselineInheritanceProperty, BaselineInheritanceValue, FamiliarState, UnknownState,
            BaselineState, FactionStatusProperty, ActiveFactionStatus, LocationStatusProperty,
            ActiveLocationStatus, LocationKindProperty, RegionLocationKind
        };
        if (fieldsAndValues.Any(value => !Token(value, 100)))
            throw new ArgumentException("A knowledge binding requires bounded JSON fields and values.");

        if (KnowledgeKinds is null || KnowledgeKinds.Count is < 1 or > 16 ||
            KnowledgeKinds.Any(value => value is null || !Token(value.ComponentTypeId, 200) ||
                !Token(value.Kind, 100) || !Token(value.PresentationKind, 100) ||
                value.ArchivedStatuses is null || value.ArchivedStatuses.Count > 20 ||
                value.ArchivedStatuses.Any(status => !Token(status, 100))) ||
            KnowledgeKinds.Select(value => value.ComponentTypeId).Distinct(StringComparer.Ordinal).Count() != KnowledgeKinds.Count ||
            KnowledgeKinds.Select(value => value.Kind).Distinct(StringComparer.Ordinal).Count() != KnowledgeKinds.Count)
            throw new ArgumentException("Knowledge kinds must be unique, bounded, and complete.");

        if (ContentStates is null || ContentStates.Count is < 1 or > 20 ||
            ContentStates.Any(value => !Token(value, 100)) ||
            ContentStates.Distinct(StringComparer.Ordinal).Count() != ContentStates.Count ||
            ContentStates.Contains(FamiliarState, StringComparer.Ordinal) ||
            ContentStates.Contains(UnknownState, StringComparer.Ordinal) ||
            FamiliarState == UnknownState ||
            !ContentStates.Contains(BaselineState, StringComparer.Ordinal))
            throw new ArgumentException("Knowledge state meanings must be bounded and disjoint.");
    }

    private static bool Token(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum &&
        !value.Any(char.IsWhiteSpace);
}

/// <summary>Ambient host selection for one application-owned binding document.</summary>
public sealed record KnowledgeApplicationSelection(string ApplicationId)
{
    public string BindingDocumentPath =>
        $"catalog/applications/{ApplicationId}/metadata/authorized-knowledge.json";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApplicationId) || ApplicationId != ApplicationId.Trim() ||
            ApplicationId.Length > 100 || ApplicationId.Any(char.IsWhiteSpace))
            throw new ArgumentException("A knowledge application selection requires one bounded application ID.");
    }
}

/// <summary>Resolves catalog/application vocabulary only after the audience policy grants access.</summary>
public interface IKnowledgeApplicationBindingResolver
{
    Task<KnowledgeApplicationBinding?> ResolveAsync(
        string campaignId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defense-in-depth campaign membership check. Participation never creates an audience grant.
/// </summary>
public interface IKnowledgeActorParticipationVerifier
{
    Task<KnowledgeParticipationResolution> ResolveAsync(
        KnowledgeApplicationBinding binding,
        string actorId,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeParticipationResolution(bool Active, string Revision)
{
    public static KnowledgeParticipationResolution Denied() => new(false, "");
}
