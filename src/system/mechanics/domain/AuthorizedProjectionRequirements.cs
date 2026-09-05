using System.Text.Json;
using System.Text.Json.Serialization;

namespace DantesRoleplay.Mechanics;

/// <summary>Opt-in, read-only materialization. All vocabulary is supplied by the application.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedProjectionRequirements
{
    public string ObserverRole { get; init; } = "";
    public string CampaignRole { get; init; } = "";
    public bool RequireActiveParticipation { get; init; }
    public string KnowledgeBinding { get; init; } = "";
    public string ContentPolicy { get; init; } = "";
    public int MaxInventoryItems { get; init; }
    public int MaxInventoryDepth { get; init; }
    public int MaxKnowledgeCandidates { get; init; }
    public int MaxSerializedOutputBytes { get; init; }
    public AuthorizedSourceSets SourceSets { get; init; } = new();

    public bool Valid(MechanicRequirements parent) =>
        ObserverRole != CampaignRole && parent.Roles.Count == 2 &&
        parent.Roles.ContainsKey(ObserverRole) && parent.Roles.ContainsKey(CampaignRole) &&
        RequireActiveParticipation && KnowledgeBinding == "application-metadata" &&
        ContentPolicy == "authorize-before-materialization" &&
        MaxInventoryItems is >= 1 and <= 512 && MaxInventoryDepth is >= 1 and <= 4 &&
        MaxKnowledgeCandidates is >= 1 and <= 10000 && MaxSerializedOutputBytes == 65536 &&
        SourceSets.Selection.InventoryRole == ObserverRole && SourceSets.Valid &&
        parent.Roles[ObserverRole].IncludeContents && parent.Roles[ObserverRole].ContentsDepth == MaxInventoryDepth &&
        !parent.Roles[CampaignRole].IncludeContents &&
        parent.Children.Count == 0 && parent.EffectComponentIds.Count == 0 && parent.Event is null &&
        parent.ElapsedTime is null && parent.Roles.Values.All(role =>
            !role.IncludeRelationships && (role.RelationshipComponents?.Count ?? 0) == 0);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedSourceSets
{
    public AuthorizedSelection Selection { get; init; } = new();
    public AuthorizedKnowledgeSources Knowledge { get; init; } = new();
    public IReadOnlyList<string> OptionalSelectedItemComponents { get; init; } = [];
    public AuthorizedDiscoverySource? Discovery { get; init; }
    public AuthorizedAssociationSource? Associations { get; init; }
    public AuthorizedActivitySource? Activities { get; init; }
    public AuthorizedMediaSource? Media { get; init; }
    public IEnumerable<string> ComponentIds() => OptionalSelectedItemComponents
        .Append(Selection.DefinitionLinkComponentId)
        .Concat(Discovery is null ? [] : new[] { Discovery.ComponentId })
        .Concat(Associations is null ? [] : new[] { Associations.CandidateComponentId })
        .Concat(Activities is null ? [] : new[] { Activities.ComponentId })
        .Concat(Activities?.Linked is { } linked ? linked.TargetComponentIds.Append(linked.ComponentId) : []);

    [JsonIgnore]
    public bool Valid => Token(Selection.ItemInputField) && Token(Selection.DefinitionLinkComponentId) &&
        Token(Selection.DefinitionLinkField) && Knowledge.BindingSource == "application-metadata" &&
        Knowledge.FilterBeforeContent && Knowledge.SubjectSources.SequenceEqual(["selected-item", "selected-definition"]) &&
        OptionalSelectedItemComponents.Count <= 32 && OptionalSelectedItemComponents.All(Token) &&
        OptionalSelectedItemComponents.Distinct(StringComparer.Ordinal).Count() == OptionalSelectedItemComponents.Count &&
        (Discovery is null || Token(Discovery.ComponentId) && Token(Discovery.RelationshipField) &&
            Token(Discovery.PropertyReferenceField) && Discovery.ValidateRelationshipObserver) &&
        (Associations is null || Token(Associations.CandidateComponentId) && Associations.RequireKnownCandidate &&
            Associations.Target == "selected-definition" && Associations.ReferencePaths.Count is >= 1 and <= 8 &&
            Associations.ReferencePaths.All(pair => Token(pair.Key) && Token(pair.Value))) &&
        (Media is null || Media.Owners is not null && Media.Owners.SequenceEqual(["selected-item", "selected-definition"])) &&
        (Activities is null || Token(Activities.ComponentId) && Token(Activities.Field) &&
            !Activities.AllowNameInference && Activities.Owners is not null &&
            Activities.Owners.SequenceEqual(["selected-item", "selected-definition"]) &&
            (Activities.Linked is not { } linked || Token(linked.ComponentId) && Token(linked.Field) &&
                linked.TargetComponentIds.Count is >= 1 and <= 32 && linked.TargetComponentIds.All(Token) &&
                linked.TargetComponentIds.Distinct(StringComparer.Ordinal).Count() == linked.TargetComponentIds.Count &&
                linked.ReferencePaths.Count <= 16 && linked.ReferencePaths.All(pair => linked.TargetComponentIds.Contains(pair.Key) &&
                    pair.Value is { Count: >= 1 and <= 8 } && pair.Value.All(Token))));
    private static bool Token(string? value) => value is { Length: >= 1 and <= 200 } &&
        value == value.Trim() && !value.Any(char.IsWhiteSpace);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedSelection(string ItemInputField = "", string InventoryRole = "",
    string DefinitionLinkComponentId = "", string DefinitionLinkField = "");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedKnowledgeSources(string BindingSource = "")
{
    public IReadOnlyList<string> SubjectSources { get; init; } = [];
    public bool FilterBeforeContent { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedDiscoverySource(string ComponentId = "", string RelationshipField = "",
    string PropertyReferenceField = "", bool ValidateRelationshipObserver = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedAssociationSource
{
    public string CandidateComponentId { get; init; } = "";
    public Dictionary<string, string> ReferencePaths { get; init; } = [];
    public string Target { get; init; } = "";
    public bool RequireKnownCandidate { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedActivitySource(string ComponentId = "", string Field = "",
    IReadOnlyList<string>? Owners = null, bool AllowNameInference = false)
{
    public AuthorizedActivityReferences? Linked { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedActivityReferences
{
    public string ComponentId { get; init; } = "";
    public string Field { get; init; } = "";
    public IReadOnlyList<string> TargetComponentIds { get; init; } = [];
    // Keys are declared component IDs; values are bounded reference paths within them.
    public Dictionary<string, IReadOnlyList<string>> ReferencePaths { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizedMediaSource
{
    public IReadOnlyList<string> Owners { get; init; } = [];
}
