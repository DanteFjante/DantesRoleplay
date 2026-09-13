using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>Filters only the declared, bounded source before rebuilding a page's graph closure.</summary>
internal static class ApplicationGraphPageSelection
{
    internal sealed record Result(IReadOnlyList<MechanicGraphNode> Nodes, int PageSize,
        string? Fingerprint = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? Facets = null);

    internal static bool TrySelect(MechanicGraphSnapshot snapshot, GraphSnapshotPageRequirement page,
        ApplicationMechanicProjectionMapping mapping, string inputJson, int offset,
        IReadOnlyList<MechanicGraphNode> nodes, out Result result, out string problem)
    {
        result = new(nodes, page.PageSize);
        problem = "The declared page selection input is invalid";
        if (page.Selection is not { } declaration) return true;
        JsonDocument input;
        try { input = JsonDocument.Parse(inputJson); }
        catch (JsonException) { return false; }
        using (input)
        {
            var value = input.RootElement;
            if (value.ValueKind != JsonValueKind.Object) return false;
            if (!value.TryGetProperty(declaration.EnabledInput, out var enabled)) return true;
            if (enabled.ValueKind != JsonValueKind.True) return false;
            if (!ReadInput(declaration.SearchInput, 200, out var search)) return false;
            var filters = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, field) in declaration.Fields)
            {
                if (field.FilterInput is null) continue;
                if (!ReadInput(field.FilterInput, 100, out var filter) ||
                    filter.Length > 0 && field.AllowedValues.Count > 0 &&
                    !field.AllowedValues.Contains(filter, StringComparer.Ordinal)) return false;
                filters[name] = filter;
            }
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { declaration, search = search.ToUpperInvariant(), filters }))));
            if (offset > 0 && (!value.TryGetProperty(declaration.ExpectedFingerprintInput, out var expected) ||
                expected.ValueKind != JsonValueKind.String || expected.GetString() != fingerprint))
            {
                problem = "The declared page selection changed between pages";
                return false;
            }

            var candidates = new List<(MechanicGraphNode Node, Dictionary<string, string> Fields)>();
            foreach (var node in nodes)
            {
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                var valid = true;
                foreach (var (name, field) in declaration.Fields)
                {
                    var values = field.Sources.Select(source => Source(node, source)).OfType<JsonElement>().ToArray();
                    if (values.Length == 0 && !field.Required) continue;
                    if (values.Length != 1 || values[0].ValueKind != JsonValueKind.String ||
                        values[0].GetString() is not { } fieldValue || fieldValue.Length == 0 ||
                        fieldValue.Length > field.MaxLength || fieldValue != fieldValue.Trim() ||
                        field.AllowedValues.Count > 0 && !field.AllowedValues.Contains(fieldValue, StringComparer.Ordinal))
                    { valid = false; break; }
                    fields[name] = fieldValue;
                }
                if (!valid || declaration.Comparisons.Any(comparison => !Compare(node, comparison))) continue;
                if (search.Length > 0 && !declaration.Fields.Any(pair => pair.Value.Search &&
                    fields.TryGetValue(pair.Key, out var fieldValue) && fieldValue.Contains(search, StringComparison.OrdinalIgnoreCase)))
                    continue;
                candidates.Add((node, fields));
            }

            bool Matches(Dictionary<string, string> fields, string? except = null) => filters.All(pair =>
                pair.Key == except || pair.Value.Length == 0 || fields.TryGetValue(pair.Key, out var field) && field == pair.Value);
            var facets = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
            foreach (var (name, field) in declaration.Fields.Where(pair => pair.Value.Facet))
            {
                var counts = field.AllowedValues.ToDictionary(item => item, _ => 0, StringComparer.Ordinal);
                foreach (var candidate in candidates.Where(candidate => Matches(candidate.Fields, name)))
                    if (candidate.Fields.TryGetValue(name, out var category)) counts[category]++;
                facets[name] = counts;
            }
            result = new(candidates.Where(candidate => Matches(candidate.Fields)).Select(candidate => candidate.Node).ToArray(),
                declaration.PageSize, fingerprint, facets);
            problem = string.Empty;
            return true;

            bool ReadInput(string name, int maximum, out string text)
            {
                text = "";
                if (!value.TryGetProperty(name, out var item)) return true;
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } raw || raw.Length > maximum) return false;
                text = raw.Trim();
                return true;
            }
            JsonElement? Source(MechanicGraphNode node, GraphPageValueSource source)
            {
                if (source.StepId is { } stepId)
                {
                    if (!snapshot.Steps.TryGetValue(stepId, out var step) || step.Nodes.Count != 1) return null;
                    node = step.Nodes[0];
                }
                if (source.EntityField is { } entityField)
                    return JsonSerializer.SerializeToElement(entityField == "id" ? node.Id : node.Name);
                var componentId = mapping.Components.TryGetValue(source.ComponentId!, out var mapped)
                    ? mapped.QualifiedTypeId : source.ComponentId!;
                if (!node.Components.TryGetValue(componentId, out var component)) return null;
                if (source.Constant is { } constant) return JsonSerializer.SerializeToElement(constant);
                foreach (var part in source.Path!.Split('/').Skip(1))
                {
                    var key = part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                    if (component.ValueKind != JsonValueKind.Object || !component.TryGetProperty(key, out component)) return null;
                }
                return component;
            }
            bool Compare(MechanicGraphNode node, GraphPageComparisonRequirement comparison)
            {
                var left = Source(node, comparison.Left);
                if (left is null) return comparison.AllowMissingLeft;
                var right = Source(node, comparison.Right);
                return left.Value.ValueKind == JsonValueKind.Number && left.Value.TryGetDecimal(out var first) &&
                    right is { ValueKind: JsonValueKind.Number } && right.Value.TryGetDecimal(out var second) &&
                    (comparison.Operator == "less-than-or-equal" ? first <= second : first > second);
            }
        }
    }
}
