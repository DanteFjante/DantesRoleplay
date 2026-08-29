using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;

namespace DantesRoleplay.SchemaValidation;

/// <summary>A closed, offline subset of JSON Schema Draft 2020-12 with hard input bounds.</summary>
public sealed class BoundedJsonSchemaValidator : IBoundedJsonSchemaValidator
{
    private static readonly HashSet<string> AllowedKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$defs", "$ref", "type", "enum", "const", "allOf", "anyOf", "oneOf", "not",
        "properties", "required", "additionalProperties", "minProperties", "maxProperties", "items",
        "prefixItems", "minItems", "maxItems", "uniqueItems", "minLength", "maxLength", "minimum",
        "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "pattern", "format",
        "propertyNames", "if", "then", "else"
    };

    public SchemaCompilationResult Compile(string schemaJson) => Compile(schemaJson, null);

    private static SchemaCompilationResult Compile(string schemaJson, string? requiredProfileId)
    {
        var diagnostics = new List<SchemaDiagnostic>();
        if (schemaJson is null || Encoding.UTF8.GetByteCount(schemaJson) > SystemJsonSchemaProfile.MaximumSchemaBytes)
            return RejectedCompilation("SCHEMA_SIZE", "The schema is absent or exceeds the byte limit.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(schemaJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = SystemJsonSchemaProfile.MaximumSchemaDepth
            });
        }
        catch (JsonException)
        {
            return RejectedCompilation("SCHEMA_JSON", "The schema is not valid bounded JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
                return RejectedCompilation("SCHEMA_ROOT", "A schema must be an object or boolean.");

            var nodeCount = 0;
            CountNodes(document.RootElement, ref nodeCount);
            if (nodeCount > SystemJsonSchemaProfile.MaximumSchemaNodes)
                return RejectedCompilation("SCHEMA_NODES", "The schema exceeds the node limit.");

            if (requiredProfileId is not null && requiredProfileId is not
                (SystemJsonSchemaProfile.Version1Id or SystemJsonSchemaProfile.Version2Id))
                return RejectedCompilation("SCHEMA_PROFILE", "The schema profile is not supported.");

            var state = new ProfileInspection(document.RootElement, diagnostics,
                requiredProfileId != SystemJsonSchemaProfile.Version1Id);
            state.InspectSchema(document.RootElement, "#");
            state.ValidateReferences();
            if (diagnostics.Count != 0)
                return RejectedCompilation(diagnostics);

            var normalized = JsonSerializer.Serialize(document.RootElement);
            try
            {
                _ = JsonSchema.FromText(normalized);
            }
            catch (Exception exception) when (exception is JsonException or JsonSchemaException)
            {
                return RejectedCompilation("SCHEMA_SEMANTICS", "The schema is not valid for the selected profile.");
            }

            var profileId = requiredProfileId ?? (state.UsesVersion2
                ? SystemJsonSchemaProfile.Version2Id
                : SystemJsonSchemaProfile.Version1Id);
            var fingerprintEnvelope = $"{{\"profile\":\"{profileId}\",\"schema\":{normalized}}}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintEnvelope)));
            return new(true, profileId, normalized, hash, []);
        }
    }

    public SchemaValueValidationResult Validate(string normalizedSchema, string valueJson) =>
        ValidateCore(Compile(normalizedSchema), valueJson);

    public SchemaValueValidationResult Validate(string profileId, string normalizedSchema, string valueJson) =>
        ValidateCore(Compile(normalizedSchema, profileId), valueJson);

    private static SchemaValueValidationResult ValidateCore(SchemaCompilationResult compilation, string valueJson)
    {
        if (!compilation.IsAccepted)
            return new(SchemaValueStatus.Rejected, compilation.Diagnostics);
        if (valueJson is null || Encoding.UTF8.GetByteCount(valueJson) > SystemJsonSchemaProfile.MaximumValueBytes)
            return RejectedValue("VALUE_SIZE", "The value is absent or exceeds the byte limit.");

        JsonDocument value;
        try
        {
            value = JsonDocument.Parse(valueJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = SystemJsonSchemaProfile.MaximumValueDepth
            });
        }
        catch (JsonException)
        {
            return RejectedValue("VALUE_JSON", "The value is not valid bounded JSON.");
        }

        using (value)
        {
            var nodes = 0;
            CountNodes(value.RootElement, ref nodes);
            if (nodes > SystemJsonSchemaProfile.MaximumValueNodes)
                return RejectedValue("VALUE_NODES", "The value exceeds the node limit.");

            try
            {
                var schema = JsonSchema.FromText(compilation.NormalizedSchema);
                var evaluation = schema.Evaluate(value.RootElement,
                    new EvaluationOptions
                    {
                        OutputFormat = OutputFormat.List,
                        RequireFormatValidation = true
                    });
                if (evaluation.IsValid) return new(SchemaValueStatus.Valid, []);

                var diagnostics = Complaints(evaluation)
                    .Take(SystemJsonSchemaProfile.MaximumDiagnostics)
                    .ToArray();
                if (diagnostics.Length == 0)
                    diagnostics = [new("VALUE_INVALID", "", "The value does not satisfy the schema.")];
                return new(SchemaValueStatus.Invalid, ReadOnly(diagnostics));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return RejectedValue("EVALUATION_REJECTED", "The bounded evaluator could not evaluate the value.");
            }
        }
    }

    private static IEnumerable<SchemaDiagnostic> Complaints(EvaluationResults result)
    {
        if (result.IsValid) yield break;
        if (result.Errors is not null)
        {
            foreach (var error in result.Errors.OrderBy(value => value.Key, StringComparer.Ordinal))
                yield return new("VALUE_INVALID", result.InstanceLocation.ToString(), error.Value);
        }
        if (result.Details is null) yield break;
        foreach (var child in result.Details)
            foreach (var diagnostic in Complaints(child)) yield return diagnostic;
    }

    private static void CountNodes(JsonElement element, ref int count)
    {
        count++;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) CountNodes(property.Value, ref count);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CountNodes(item, ref count);
    }

    private static SchemaCompilationResult RejectedCompilation(string code, string message) =>
        RejectedCompilation([new(code, "", message)]);

    private static SchemaCompilationResult RejectedCompilation(IEnumerable<SchemaDiagnostic> diagnostics) =>
        new(false, SystemJsonSchemaProfile.Id, "", "", ReadOnly(diagnostics.Take(SystemJsonSchemaProfile.MaximumDiagnostics)));

    private static SchemaValueValidationResult RejectedValue(string code, string message) =>
        new(SchemaValueStatus.Rejected, ReadOnly<SchemaDiagnostic>([new(code, "", message)]));

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());

    private sealed class ProfileInspection(
        JsonElement root,
        List<SchemaDiagnostic> diagnostics,
        bool allowVersion2)
    {
        private readonly List<(string Source, string Target)> _references = [];
        private int _definitionCount;
        private int _patternCount;

        public bool UsesVersion2 { get; private set; }

        public void InspectSchema(JsonElement schema, string pointer)
        {
            if (diagnostics.Count >= SystemJsonSchemaProfile.MaximumDiagnostics ||
                schema.ValueKind is JsonValueKind.True or JsonValueKind.False) return;
            if (schema.ValueKind != JsonValueKind.Object)
            {
                Add("SCHEMA_SHAPE", pointer, "A nested schema must be an object or boolean.");
                return;
            }

            foreach (var property in schema.EnumerateObject())
            {
                var childPointer = pointer + "/" + Escape(property.Name);
                if (!AllowedKeywords.Contains(property.Name))
                {
                    Add("SCHEMA_KEYWORD", childPointer, "The schema contains an unsupported keyword.");
                    continue;
                }

                if (property.Name is "pattern" or "format")
                {
                    if (!allowVersion2)
                    {
                        Add("SCHEMA_KEYWORD", childPointer, "The schema contains a keyword unsupported by this profile.");
                        continue;
                    }
                    UsesVersion2 = true;
                }

                switch (property.Name)
                {
                    case "$schema":
                        if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() != SystemJsonSchemaProfile.MetaSchemaUri)
                            Add("SCHEMA_DIALECT", childPointer, "The schema dialect must be the official Draft 2020-12 URI.");
                        break;
                    case "$ref":
                        if (property.Value.ValueKind != JsonValueKind.String || !IsLocalReference(property.Value.GetString()!))
                            Add("SCHEMA_REFERENCE", childPointer, "Only same-document JSON fragment references are allowed.");
                        else
                            _references.Add((pointer, property.Value.GetString()!));
                        break;
                    case "$defs":
                    case "properties":
                        InspectSchemaMap(property.Value, childPointer, property.Name == "$defs");
                        break;
                    case "allOf":
                    case "anyOf":
                    case "oneOf":
                    case "prefixItems":
                        InspectSchemaArray(property.Value, childPointer);
                        break;
                    case "not":
                    case "items":
                    case "additionalProperties":
                    case "propertyNames":
                    case "if":
                    case "then":
                    case "else":
                        InspectSchema(property.Value, childPointer);
                        break;
                    case "pattern":
                        InspectPattern(schema, property.Value, childPointer);
                        break;
                    case "format":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            property.Value.GetString() != "date-time")
                            Add("SCHEMA_FORMAT", childPointer, "Only the asserted date-time format is supported.");
                        break;
                }
            }
        }

        public void ValidateReferences()
        {
            if (_definitionCount > SystemJsonSchemaProfile.MaximumDefinitions)
                Add("SCHEMA_DEFINITIONS", "#/$defs", "The schema exceeds the definition limit.");
            if (_references.Count > SystemJsonSchemaProfile.MaximumReferences)
                Add("SCHEMA_REFERENCES", "", "The schema exceeds the reference limit.");
            if (diagnostics.Count != 0) return;

            foreach (var reference in _references)
                if (!TryResolve(reference.Target, out _))
                    Add("SCHEMA_REFERENCE_MISSING", reference.Source, "A fragment reference does not resolve in this schema.");
            if (diagnostics.Count != 0) return;

            // Recursive local references are safe because both schema and value depth/node limits
            // are enforced before the evaluator runs. They are needed for bounded recursive data
            // structures such as a nested prerequisite expression; external references remain
            // forbidden and cannot expand the evaluator's trust boundary.
        }

        private void InspectSchemaMap(JsonElement value, string pointer, bool definitions)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                Add("SCHEMA_SHAPE", pointer, "This keyword requires an object of schemas.");
                return;
            }
            foreach (var property in value.EnumerateObject())
            {
                if (definitions) _definitionCount++;
                InspectSchema(property.Value, pointer + "/" + Escape(property.Name));
            }
        }

        private void InspectSchemaArray(JsonElement value, string pointer)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                Add("SCHEMA_SHAPE", pointer, "This keyword requires an array of schemas.");
                return;
            }
            var index = 0;
            foreach (var item in value.EnumerateArray()) InspectSchema(item, pointer + "/" + index++);
        }

        private void InspectPattern(JsonElement schema, JsonElement value, string pointer)
        {
            _patternCount++;
            if (_patternCount > SystemJsonSchemaProfile.MaximumPatterns)
            {
                Add("SCHEMA_PATTERNS", pointer, "The schema exceeds the pattern count limit.");
                return;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                Add("SCHEMA_PATTERN", pointer, "A pattern must be a string.");
                return;
            }

            var pattern = value.GetString()!;
            if (pattern == "\\S")
            {
                if (!HasBoundedStringLength(schema, out _))
                    Add("SCHEMA_PATTERN", pointer, "A non-whitespace pattern requires a bounded sibling maxLength.");
                return;
            }

            if (pattern.Length is < 3 or > SystemJsonSchemaProfile.MaximumPatternLength ||
                pattern[0] != '^' || pattern[^1] != '$')
            {
                Add("SCHEMA_PATTERN", pointer, "A pattern must be a bounded anchored expression.");
                return;
            }

            var index = 1;
            var end = pattern.Length - 1;
            if (!InspectPatternSequence(pattern, ref index, end, schema, 0) || index != end)
                Add("SCHEMA_PATTERN", pointer, "The pattern uses syntax outside the bounded expression grammar.");
        }

        private static bool InspectPatternSequence(
            string pattern,
            ref int index,
            int end,
            JsonElement schema,
            int groupDepth)
        {
            var expectAtom = true;
            while (index < end && pattern[index] != ')')
            {
                if (pattern[index] == '|')
                {
                    if (groupDepth == 0 || expectAtom) return false;
                    index++;
                    expectAtom = true;
                    continue;
                }

                if (!InspectPatternAtom(pattern, ref index, end, schema, groupDepth)) return false;
                expectAtom = false;
            }
            return !expectAtom;
        }

        private static bool InspectPatternAtom(
            string pattern,
            ref int index,
            int end,
            JsonElement schema,
            int groupDepth)
        {
            if (pattern[index] == '\\')
            {
                if (++index >= end || pattern[index] is not ('.' or '-' or '_' or '/' or ':' or 'S')) return false;
                index++;
            }
            else if (pattern[index] == '[')
            {
                var close = pattern.IndexOf(']', index + 1);
                if (close <= index + 1 || close >= end) return false;
                for (var i = index + 1; i < close; i++)
                    if (!(char.IsAsciiLetterOrDigit(pattern[i]) || pattern[i] is '.' or '-' or '_')) return false;
                index = close + 1;
            }
            else if (pattern[index] == '(')
            {
                if (groupDepth >= 4 || index + 2 >= end || pattern[index + 1] != '?' || pattern[index + 2] != ':') return false;
                index += 3;
                if (!InspectPatternSequence(pattern, ref index, end, schema, groupDepth + 1) ||
                    index >= end || pattern[index] != ')') return false;
                index++;
            }
            else if (!(char.IsAsciiLetterOrDigit(pattern[index]) || pattern[index] is '_' or '-' or '/' or ':'))
                return false;

            else index++;

            if (index >= end) return true;
            if (pattern[index] is '*' or '+')
            {
                if (!HasBoundedStringLength(schema, out _)) return false;
                index++;
                return true;
            }
            if (pattern[index] == '?')
            {
                index++;
                return true;
            }
            if (pattern[index] != '{') return true;

            var closeBrace = pattern.IndexOf('}', index + 1);
            if (closeBrace < 0 || closeBrace >= end) return false;
            var range = pattern.AsSpan(index + 1, closeBrace - index - 1);
            var comma = range.IndexOf(',');
            var minimumText = comma < 0 ? range : range[..comma];
            var maximumText = comma < 0 ? range : range[(comma + 1)..];
            if (!int.TryParse(minimumText, out var minimum) || !int.TryParse(maximumText, out var maximum) ||
                minimum < 0 || maximum < minimum || maximum > SystemJsonSchemaProfile.MaximumPatternRepetition)
                return false;
            index = closeBrace + 1;
            return true;
        }

        private static bool HasBoundedStringLength(JsonElement schema, out int maximum)
        {
            maximum = 0;
            return schema.TryGetProperty("maxLength", out var maxLength) &&
                maxLength.ValueKind == JsonValueKind.Number && maxLength.TryGetInt32(out maximum) &&
                maximum is >= 1 and <= SystemJsonSchemaProfile.MaximumUnboundedPatternValueLength;
        }

        private bool TryResolve(string reference, out JsonElement value)
        {
            value = root;
            if (reference == "#") return true;
            string decoded;
            try { decoded = Uri.UnescapeDataString(reference[1..]); }
            catch (UriFormatException) { return false; }
            if (!decoded.StartsWith("/", StringComparison.Ordinal)) return false;
            foreach (var raw in decoded[1..].Split('/'))
            {
                var segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(segment, out var property)) value = property;
                else if (value.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index) && index >= 0 && index < value.GetArrayLength()) value = value[index];
                else return false;
            }
            return value.ValueKind is JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False;
        }

        private static bool IsLocalReference(string reference) =>
            reference == "#" || reference.StartsWith("#/", StringComparison.Ordinal);

        private void Add(string code, string pointer, string message)
        {
            if (diagnostics.Count < SystemJsonSchemaProfile.MaximumDiagnostics)
                diagnostics.Add(new(code, pointer, message));
        }

        private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }
}
