using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;

namespace DantesRoleplay.CatalogNavigation;

public enum ReadableRuleAudience { Public, Dm }

public sealed record ReadableRulesRequest(
    ApplicationIdentifier ApplicationId,
    ReadableRuleAudience Audience = ReadableRuleAudience.Public);

public sealed record ReadableRuleBlockView(
    string Kind,
    string? Heading,
    string? Body,
    IReadOnlyList<string> Items);

public sealed record ReadableRuleExampleView(string Title, string Body);

public sealed record ReadableRuleCitationView(string SourceId, string Locator);

public sealed record ReadableRuleAuthorityView(
    IReadOnlyList<string> MechanicIds,
    IReadOnlyList<string> ProcedureIds);

public sealed record ReadableRuleSourceView(
    string OwnerId,
    string Label,
    string Classification);

public sealed record ReadableRuleView(
    string Id,
    string ResolutionKey,
    string Title,
    string Summary,
    int Order,
    IReadOnlyList<ReadableRuleBlockView> Blocks,
    IReadOnlyList<ReadableRuleExampleView> Examples,
    IReadOnlyList<string> RelatedRuleIds,
    IReadOnlyList<ReadableRuleCitationView> Citations,
    ReadableRuleAuthorityView Authority,
    string Visibility,
    ReadableRuleSourceView Source);

public sealed record ReadableRuleSectionView(
    string Id,
    string Label,
    int Order,
    IReadOnlyList<ReadableRuleView> Rules);

public sealed record ReadableRulesResult(
    string ApplicationId,
    string ResolutionFingerprint,
    string RulesFingerprint,
    string Audience,
    IReadOnlyList<ReadableRuleSectionView> Sections);

internal static class ReadableRuleCatalogProjection
{
    internal const string ComponentId = "game.core.rules.readable";
    private const int MaximumRules = 4_096;
    private static readonly HashSet<string> BlockKinds =
        ["paragraph", "steps", "list", "callout"];

    internal static ReadableRulesResult Project(
        CatalogNavigationManifest manifest,
        CatalogExtensionResolutionContext? resolution,
        ReadableRulesRequest request)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ApplicationId != manifest.ApplicationId || !Enum.IsDefined(request.Audience))
            throw new ArgumentException("The readable-rules request is outside this application.", nameof(request));

        var selected = CatalogExtensionSearch.Apply(
            resolution,
            manifest.Records.Where(value => value.Kind == "entity" && value.Status == "active").ToArray(),
            value => value.QualifiedId,
            value => value.Kind).Records;
        var extensions = (resolution?.Extensions ?? []).ToDictionary(
            value => value.ExtensionId, StringComparer.Ordinal);
        var rules = new List<ProjectedRule>();
        foreach (var record in selected)
        {
            var parsed = Parse(record);
            if (parsed is null || parsed.PresentationStatus != "published"
                || parsed.Visibility == "dm" && request.Audience != ReadableRuleAudience.Dm)
                continue;
            if (rules.Count == MaximumRules)
                throw new InvalidOperationException("The resolved readable-rules catalog exceeds its bound.");
            var identity = resolution is null
                ? (Owner: "base", Key: record.QualifiedId[(manifest.ApplicationId.Value.Length + 1)..])
                : CatalogExtensionSearch.OwnerAndKey(resolution, record.QualifiedId);
            var extension = identity.Owner == "base" ? null : extensions[identity.Owner];
            rules.Add(new(parsed.SectionId, parsed.SectionLabel, parsed.SectionOrder,
                new(record.QualifiedId, identity.Key, parsed.Title, parsed.Summary, parsed.RuleOrder,
                    parsed.Blocks, parsed.Examples, parsed.RelatedRuleIds, parsed.Citations,
                    new(parsed.MechanicIds, parsed.ProcedureIds), parsed.Visibility,
                    new(identity.Owner, extension?.DisplayName ?? "Core",
                        extension?.Classification ?? "core"))));
        }

        var sections = rules.GroupBy(value => value.SectionId, StringComparer.Ordinal)
            .Select(group =>
            {
                if (group.Select(value => (value.SectionLabel, value.SectionOrder)).Distinct().Count() != 1)
                    throw new InvalidOperationException(
                        $"Readable-rule section '{group.Key}' has conflicting labels or ordering.");
                var first = group.First();
                return new ReadableRuleSectionView(group.Key, first.SectionLabel, first.SectionOrder,
                    Array.AsReadOnly(group.Select(value => value.Rule)
                        .OrderBy(value => value.Order)
                        .ThenBy(value => value.Title, StringComparer.Ordinal)
                        .ThenBy(value => value.Id, StringComparer.Ordinal).ToArray()));
            })
            .OrderBy(value => value.Order)
            .ThenBy(value => value.Label, StringComparer.Ordinal)
            .ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
        var fingerprintJson = JsonSerializer.Serialize(new
        {
            applicationId = manifest.ApplicationId.Value,
            resolutionFingerprint = resolution?.Fingerprint ?? "none",
            audience = request.Audience.ToString().ToLowerInvariant(),
            sections
        });
        return new(manifest.ApplicationId.Value, resolution?.Fingerprint ?? "none",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintJson))),
            request.Audience.ToString().ToLowerInvariant(), Array.AsReadOnly(sections));
    }

    private static ParsedRule? Parse(CatalogRecordDefinition record)
    {
        using var document = JsonDocument.Parse(record.ContentJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var id) || id.GetString() != record.QualifiedId
            || !root.TryGetProperty("components", out var components)
            || components.ValueKind != JsonValueKind.Object
            || !components.TryGetProperty(ComponentId, out var component)) return null;
        RequireObject(component, ["section", "order", "title", "summary", "blocks", "examples",
            "relatedRuleRefs", "citations", "mechanicIds", "procedureIds", "visibility",
            "presentationStatus"]);
        var section = component.GetProperty("section");
        RequireObject(section, ["id", "label", "order"]);
        var sectionId = Text(section, "id", 100, identifier: true);
        var sectionLabel = Text(section, "label", 160);
        var sectionOrder = Integer(section, "order");
        var ruleOrder = Integer(component, "order");
        var title = Text(component, "title", 200);
        var summary = Text(component, "summary", 2_000);
        var visibility = Text(component, "visibility", 20);
        var presentationStatus = Text(component, "presentationStatus", 20);
        if (visibility is not ("public" or "dm")
            || presentationStatus is not ("draft" or "published" or "retired"))
            throw Invalid(record, "visibility or presentation status");

        var blocks = Elements(component, "blocks", 1, 64).Select(value =>
        {
            RequireObject(value, ["kind", "heading", "body", "items"]);
            var kind = Text(value, "kind", 20);
            var heading = NullableText(value, "heading", 200);
            var body = NullableText(value, "body", 10_000);
            var items = TextArray(value, "items", 64, 1_000);
            if (!BlockKinds.Contains(kind) || body is null && items.Count == 0
                || kind is "steps" or "list" && items.Count == 0)
                throw Invalid(record, "readable block");
            return new ReadableRuleBlockView(kind, heading, body, items);
        }).ToArray();
        var examples = Elements(component, "examples", 0, 32).Select(value =>
        {
            RequireObject(value, ["title", "body"]);
            return new ReadableRuleExampleView(Text(value, "title", 200), Text(value, "body", 5_000));
        }).ToArray();
        var related = Elements(component, "relatedRuleRefs", 0, 32).Select(value =>
        {
            RequireObject(value, ["entityId"]);
            return Text(value, "entityId", 400, qualified: true);
        }).ToArray();
        var citations = Elements(component, "citations", 1, 32).Select(value =>
        {
            RequireObject(value, ["sourceId", "locator"]);
            return new ReadableRuleCitationView(Text(value, "sourceId", 200), Text(value, "locator", 1_000));
        }).ToArray();
        var mechanicIds = TextArray(component, "mechanicIds", 32, 400, qualified: true);
        var procedureIds = TextArray(component, "procedureIds", 32, 400, qualified: true);
        if (mechanicIds.Count + procedureIds.Count == 0)
            throw Invalid(record, "authoritative mechanic or procedure link");
        EnsureDistinct(record, related, "related rules");
        EnsureDistinct(record, mechanicIds, "mechanics");
        EnsureDistinct(record, procedureIds, "procedures");
        return new(sectionId, sectionLabel, sectionOrder, ruleOrder, title, summary,
            Array.AsReadOnly(blocks), Array.AsReadOnly(examples), related,
            Array.AsReadOnly(citations), mechanicIds, procedureIds, visibility, presentationStatus);
    }

    private static IReadOnlyList<JsonElement> Elements(JsonElement owner, string name, int minimum, int maximum)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array) throw new JsonException($"'{name}' must be an array.");
        var values = value.EnumerateArray().ToArray();
        if (values.Length < minimum || values.Length > maximum) throw new JsonException($"'{name}' is outside its bound.");
        return values;
    }

    private static IReadOnlyList<string> TextArray(
        JsonElement owner, string name, int maximum, int textMaximum, bool qualified = false)
    {
        var values = Elements(owner, name, 0, maximum)
            .Select(value => Text(value, textMaximum, qualified: qualified)).ToArray();
        return Array.AsReadOnly(values);
    }

    private static string Text(JsonElement owner, string name, int maximum,
        bool identifier = false, bool qualified = false) =>
        Text(owner.GetProperty(name), maximum, identifier, qualified);

    private static string Text(JsonElement value, int maximum,
        bool identifier = false, bool qualified = false)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (text is null || text.Length is < 1 || text.Length > maximum || text != text.Trim()
            || text.Any(char.IsControl)
            || identifier && !CatalogNavigationManifest.IsIdentifier(text)
            || qualified && (text.Split('.').Length < 2
                || text.Split('.').Any(segment => !CatalogNavigationManifest.IsIdentifier(segment))))
            throw new JsonException("Readable-rule text is invalid or unbounded.");
        return text;
    }

    private static string? NullableText(JsonElement owner, string name, int maximum)
    {
        var value = owner.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : Text(value, maximum);
    }

    private static int Integer(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (!value.TryGetInt32(out var result) || result is < 0 or > 10_000)
            throw new JsonException($"'{name}' is outside its ordering bound.");
        return result;
    }

    private static void RequireObject(JsonElement value, string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("Readable-rule objects must use their exact closed shape.");
        var actual = value.EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(keys))
            throw new JsonException("Readable-rule objects must use their exact closed shape.");
    }

    private static void EnsureDistinct(CatalogRecordDefinition record,
        IReadOnlyList<string> values, string label)
    {
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw Invalid(record, label);
    }

    private static InvalidOperationException Invalid(CatalogRecordDefinition record, string field) =>
        new($"Readable rule '{record.QualifiedId}' has invalid {field}.");

    private sealed record ParsedRule(
        string SectionId, string SectionLabel, int SectionOrder, int RuleOrder,
        string Title, string Summary, IReadOnlyList<ReadableRuleBlockView> Blocks,
        IReadOnlyList<ReadableRuleExampleView> Examples, IReadOnlyList<string> RelatedRuleIds,
        IReadOnlyList<ReadableRuleCitationView> Citations, IReadOnlyList<string> MechanicIds,
        IReadOnlyList<string> ProcedureIds, string Visibility, string PresentationStatus);

    private sealed record ProjectedRule(
        string SectionId, string SectionLabel, int SectionOrder, ReadableRuleView Rule);
}
