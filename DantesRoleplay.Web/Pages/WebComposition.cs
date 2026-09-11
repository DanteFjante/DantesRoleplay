using System.Collections.ObjectModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DantesRoleplay.Web.Pages;

/// <summary>
/// Page-local, declarative composition format. It deliberately has no storage, publication,
/// authorization, query-execution, or action-execution dependency.
/// </summary>
public static class WebComposition
{
    public const int FormatVersion = 1;
    public const int MaximumDocumentBytes = 1024 * 1024;
    public const int MaximumComponents = 64;
    public const int MaximumNodes = 4096;
    public const int MaximumDepth = 32;
    public const int MaximumLoopItems = 100;
    public const int MaximumRenderedCharacters = 1024 * 1024;
    public const int MaximumExpandedRenderNodes = 16_384;
}

public sealed record WebCompositionError(string Path, string Code, string Message);

public sealed record WebCompositionParseResult(
    WebCompositionDocument? Document,
    IReadOnlyList<WebCompositionError> Errors)
{
    public bool IsValid => Document is not null && Errors.Count == 0;
}

public sealed record WebCompositionRenderResult(string? Html, IReadOnlyList<WebCompositionError> Errors)
{
    public bool IsSuccess => Html is not null && Errors.Count == 0;
}

internal sealed record WebCompositionComponentKey(string Id, string Revision)
{
    public override string ToString() => $"{Id}@{Revision}";
}

public sealed record WebCompositionQueryBinding(string Name);
public sealed record WebCompositionActionBinding(string Name);

internal sealed record WebCompositionComponent(
    WebCompositionComponentKey Key,
    IReadOnlySet<string> RequiredProps,
    WebCompositionNode Template);

public sealed class WebCompositionDocument
{
    internal WebCompositionDocument(string generation, WebCompositionNode root,
        IReadOnlyDictionary<WebCompositionComponentKey, WebCompositionComponent> components,
        IReadOnlyDictionary<string, WebCompositionQueryBinding> queryBindings,
        IReadOnlyDictionary<string, WebCompositionActionBinding> actionBindings)
    {
        Generation = generation;
        Root = root;
        Components = components;
        QueryBindings = queryBindings;
        ActionBindings = actionBindings;
    }

    public string Generation { get; }
    public IReadOnlyDictionary<string, WebCompositionQueryBinding> QueryBindings { get; }
    public IReadOnlyDictionary<string, WebCompositionActionBinding> ActionBindings { get; }
    internal WebCompositionNode Root { get; }
    internal IReadOnlyDictionary<WebCompositionComponentKey, WebCompositionComponent> Components { get; }
}

internal abstract record WebCompositionNode;
internal sealed record WebCompositionElementNode(
    string Tag,
    IReadOnlyDictionary<string, string> Attributes,
    string? Action,
    IReadOnlyList<WebCompositionNode> Children) : WebCompositionNode;
internal sealed record WebCompositionTextNode(string Text) : WebCompositionNode;
internal sealed record WebCompositionValueNode(string Path) : WebCompositionNode;
internal sealed record WebCompositionAssetNode(string Path) : WebCompositionNode;
internal sealed record WebCompositionIfNode(
    string Condition,
    IReadOnlyList<WebCompositionNode> Children,
    IReadOnlyList<WebCompositionNode> Otherwise) : WebCompositionNode;
internal sealed record WebCompositionEachNode(
    string Items,
    string ItemName,
    IReadOnlyList<WebCompositionNode> Children) : WebCompositionNode;
internal sealed record WebCompositionSlotNode(string Name, IReadOnlyList<WebCompositionNode> Fallback) : WebCompositionNode;
internal sealed record WebCompositionComponentNode(
    WebCompositionComponentKey Component,
    IReadOnlyDictionary<string, JsonElement> Props,
    IReadOnlyDictionary<string, IReadOnlyList<WebCompositionNode>> Slots) : WebCompositionNode;

public sealed class WebCompositionParser
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal)
    {
        "a", "article", "button", "div", "footer", "h1", "h2", "h3", "header", "img",
        "li", "main", "nav", "ol", "p", "section", "span", "strong", "ul"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.Ordinal)
    {
        "alt", "aria-label", "class", "href", "id", "role", "src", "title"
    };

    public WebCompositionParseResult Parse(string json, IEnumerable<string>? availableAssetPaths = null)
    {
        var errors = new List<WebCompositionError>();
        if (json is null)
        {
            errors.Add(new("$", "MISSING_DOCUMENT", "The composition document is required."));
            return new(null, errors.AsReadOnly());
        }
        if (Encoding.UTF8.GetByteCount(json) > WebComposition.MaximumDocumentBytes)
        {
            errors.Add(new("$", "DOCUMENT_TOO_LARGE", "The composition document exceeds its byte budget."));
            return new(null, errors.AsReadOnly());
        }

        try
        {
            using var source = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = WebComposition.MaximumDepth
            });
            EnsureNoDuplicateProperties(source.RootElement, "$", 0);
            var assets = new HashSet<string>(availableAssetPaths ?? [], StringComparer.Ordinal);
            foreach (var asset in assets)
                if (!WebPageAssetPath.TryValidate(asset, out _) || !asset.StartsWith("assets/", StringComparison.Ordinal))
                    throw Error("$", "INVALID_AVAILABLE_ASSET", "Available assets must be exact safe asset paths.");
            var budget = new ParseBudget();
            var document = ParseDocument(source.RootElement, assets, budget);
            ValidateReferences(document, budget);
            return new(document, errors.AsReadOnly());
        }
        catch (CompositionException exception)
        {
            errors.Add(new(exception.Path, exception.Code, exception.Message));
        }
        catch (JsonException exception)
        {
            errors.Add(new("$", "INVALID_JSON", exception.Message));
        }
        return new(null, errors.AsReadOnly());
    }

    private static WebCompositionDocument ParseDocument(JsonElement value, ISet<string> assets, ParseBudget budget)
    {
        RequireObject(value, "$", "DOCUMENT_OBJECT_REQUIRED");
        RejectUnknown(value, "$", "formatVersion", "generation", "root", "components", "queries", "actions");
        if (!Required(value, "formatVersion", "$", JsonValueKind.Number).TryGetInt32(out var formatVersion) ||
            formatVersion != WebComposition.FormatVersion)
            throw Error("$.formatVersion", "UNSUPPORTED_FORMAT_VERSION", "Only composition formatVersion 1 is supported.");
        var generation = RequiredString(value, "generation", "$", 1, 200);
        var root = ParseNode(Required(value, "root", "$", JsonValueKind.Object), "$.root", assets, budget, 1);
        var components = ParseComponents(Required(value, "components", "$", JsonValueKind.Array), assets, budget);
        var queries = ParseQueryBindings(value);
        var actions = ParseActionBindings(value);
        return new(generation, root, components, queries, actions);
    }

    private static IReadOnlyDictionary<WebCompositionComponentKey, WebCompositionComponent> ParseComponents(
        JsonElement value, ISet<string> assets, ParseBudget budget)
    {
        if (value.GetArrayLength() > WebComposition.MaximumComponents)
            throw Error("$.components", "COMPONENT_LIMIT_EXCEEDED", "The component limit is 64.");
        var result = new Dictionary<WebCompositionComponentKey, WebCompositionComponent>();
        var index = 0;
        foreach (var component in value.EnumerateArray())
        {
            var path = $"$.components[{index++}]";
            RequireObject(component, path, "COMPONENT_OBJECT_REQUIRED");
            RejectUnknown(component, path, "id", "revision", "requiredProps", "template");
            var key = new WebCompositionComponentKey(
                RequiredString(component, "id", path, 1, 100), RequiredString(component, "revision", path, 1, 100));
            if (!result.TryAdd(key, new(key, ParseNames(component, "requiredProps", path),
                    ParseNode(Required(component, "template", path, JsonValueKind.Object), path + ".template", assets, budget, 1))))
                throw Error(path, "DUPLICATE_COMPONENT", $"Component '{key}' is declared more than once.");
        }
        return new ReadOnlyDictionary<WebCompositionComponentKey, WebCompositionComponent>(result);
    }

    private static IReadOnlyDictionary<string, WebCompositionQueryBinding> ParseQueryBindings(JsonElement source) =>
        ParseBindings(source, "queries", "$.queries", name => new WebCompositionQueryBinding(name));

    private static IReadOnlyDictionary<string, WebCompositionActionBinding> ParseActionBindings(JsonElement source) =>
        ParseBindings(source, "actions", "$.actions", name => new WebCompositionActionBinding(name));

    private static IReadOnlyDictionary<string, T> ParseBindings<T>(JsonElement source, string name, string path, Func<string, T> create)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        if (!source.TryGetProperty(name, out var values))
            return new ReadOnlyDictionary<string, T>(result);
        if (values.ValueKind != JsonValueKind.Array)
            throw Error(path, "BINDING_LIST_REQUIRED", "Bindings must be an array.");
        if (values.GetArrayLength() > 16)
            throw Error(path, "BINDING_LIMIT_EXCEEDED", "The binding limit is 16.");
        var index = 0;
        foreach (var binding in values.EnumerateArray())
        {
            var itemPath = $"{path}[{index++}]";
            RequireObject(binding, itemPath, "BINDING_OBJECT_REQUIRED");
            RejectUnknown(binding, itemPath, "name");
            var bindingName = RequiredString(binding, "name", itemPath, 1, 100);
            if (!IsIdentifier(bindingName) || bindingName == "props")
                throw Error(itemPath + ".name", "INVALID_BINDING_NAME", "Binding names must be ASCII identifiers other than 'props'.");
            var declared = create(bindingName);
            if (!result.TryAdd(bindingName, declared))
                throw Error(itemPath, "DUPLICATE_BINDING", $"Binding '{bindingName}' is declared more than once.");
        }
        return new ReadOnlyDictionary<string, T>(result);
    }

    private static IReadOnlySet<string> ParseNames(JsonElement source, string name, string path)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!source.TryGetProperty(name, out var values)) return new ReadOnlySet<string>(result);
        if (values.ValueKind != JsonValueKind.Array)
            throw Error(path + "." + name, "NAME_LIST_REQUIRED", "Required properties must be an array.");
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !IsIdentifier(value.GetString() ?? string.Empty))
                throw Error(path + "." + name, "INVALID_NAME", "Names must be non-empty strings.");
            if (!result.Add(value.GetString()!))
                throw Error(path + "." + name, "DUPLICATE_NAME", "Names may be declared only once.");
        }
        return new ReadOnlySet<string>(result);
    }

    private static WebCompositionNode ParseNode(JsonElement value, string path, ISet<string> assets, ParseBudget budget, int depth)
    {
        if (depth > WebComposition.MaximumDepth) throw Error(path, "DEPTH_LIMIT_EXCEEDED", "The node depth limit is 32.");
        if (++budget.Nodes > WebComposition.MaximumNodes) throw Error(path, "NODE_LIMIT_EXCEEDED", "The node limit is 4096.");
        RequireObject(value, path, "NODE_OBJECT_REQUIRED");
        var kind = RequiredString(value, "kind", path, 1, 20);
        return kind switch
        {
            "element" => ParseElement(value, path, assets, budget, depth),
            "text" => ParseText(value, path),
            "value" => ParseValue(value, path),
            "asset" => ParseAsset(value, path, assets),
            "if" => ParseIf(value, path, assets, budget, depth),
            "each" => ParseEach(value, path, assets, budget, depth),
            "slot" => ParseSlot(value, path, assets, budget, depth),
            "component" => ParseComponentReference(value, path, assets, budget, depth),
            _ => throw Error(path + ".kind", "UNKNOWN_NODE_KIND", $"'{kind}' is not a supported node kind.")
        };
    }

    private static WebCompositionNode ParseElement(JsonElement value, string path, ISet<string> assets, ParseBudget budget, int depth)
    {
        RejectUnknown(value, path, "kind", "tag", "attributes", "action", "children");
        var tag = RequiredString(value, "tag", path, 1, 30);
        if (!AllowedTags.Contains(tag)) throw Error(path + ".tag", "UNSAFE_HTML_TAG", $"HTML tag '{tag}' is not allowed.");
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.TryGetProperty("attributes", out var attrs))
        {
            RequireObject(attrs, path + ".attributes", "ATTRIBUTES_OBJECT_REQUIRED");
            foreach (var attr in attrs.EnumerateObject())
            {
                if (!AllowedAttributes.Contains(attr.Name) || attr.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    throw Error(path + ".attributes." + attr.Name, "UNSAFE_HTML_ATTRIBUTE", "The HTML attribute is not allowed.");
                if (attr.Value.ValueKind != JsonValueKind.String)
                    throw Error(path + ".attributes." + attr.Name, "ATTRIBUTE_STRING_REQUIRED", "Attribute values must be strings.");
                var attrValue = attr.Value.GetString()!;
                ValidateAttributeValue(tag, attr.Name, attrValue, path + ".attributes." + attr.Name, assets);
                ValidateAssetReference(attrValue, assets, path + ".attributes." + attr.Name);
                attributes.Add(attr.Name, attrValue);
            }
        }
        string? action = null;
        if (value.TryGetProperty("action", out var actionValue))
        {
            if (actionValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(actionValue.GetString()))
                throw Error(path + ".action", "INVALID_ACTION_NAME", "An action must be a non-empty binding name.");
            if (tag != "button")
                throw Error(path + ".action", "ACTION_REQUIRES_BUTTON", "Actions are only allowed on button elements.");
            action = actionValue.GetString();
        }
        var children = ParseChildren(value, "children", path, assets, budget, depth);
        if (tag == "img" && children.Count != 0)
            throw Error(path + ".children", "VOID_ELEMENT_CHILDREN", "Image elements cannot have children.");
        return new WebCompositionElementNode(tag, new ReadOnlyDictionary<string, string>(attributes), action, children);
    }

    private static WebCompositionNode ParseText(JsonElement value, string path)
    {
        RejectUnknown(value, path, "kind", "text");
        return new WebCompositionTextNode(RequiredString(value, "text", path, 0, 20_000));
    }

    private static WebCompositionNode ParseValue(JsonElement value, string path)
    {
        RejectUnknown(value, path, "kind", "path");
        return new WebCompositionValueNode(RequiredPath(value, "path", path));
    }

    private static WebCompositionNode ParseAsset(JsonElement value, string path, ISet<string> assets)
    {
        RejectUnknown(value, path, "kind", "path");
        var asset = RequiredString(value, "path", path, 1, WebPageBundleLimits.MaximumAssetPathLength);
        ValidateAssetReference("asset:" + asset, assets, path + ".path");
        return new WebCompositionAssetNode(asset);
    }

    private static WebCompositionNode ParseIf(JsonElement value, string path, ISet<string> assets, ParseBudget budget, int depth)
    {
        RejectUnknown(value, path, "kind", "condition", "children", "otherwise");
        var children = ParseChildren(value, "children", path, assets, budget, depth);
        var otherwise = value.TryGetProperty("otherwise", out var ignored)
            ? ParseChildren(value, "otherwise", path, assets, budget, depth)
            : Array.Empty<WebCompositionNode>();
        return new WebCompositionIfNode(RequiredPath(value, "condition", path), children, otherwise);
    }

    private static WebCompositionNode ParseEach(JsonElement value, string path, ISet<string> assets, ParseBudget budget, int depth)
    {
        RejectUnknown(value, path, "kind", "items", "as", "children");
        var item = RequiredString(value, "as", path, 1, 100);
        if (!IsIdentifier(item)) throw Error(path + ".as", "INVALID_LOOP_NAME", "The loop name must be an identifier.");
        return new WebCompositionEachNode(RequiredPath(value, "items", path), item,
            ParseChildren(value, "children", path, assets, budget, depth));
    }

    private static WebCompositionNode ParseSlot(JsonElement value, string path, ISet<string> assets, ParseBudget budget, int depth)
    {
        RejectUnknown(value, path, "kind", "name", "fallback");
        var fallback = value.TryGetProperty("fallback", out var ignored)
            ? ParseChildren(value, "fallback", path, assets, budget, depth)
            : Array.Empty<WebCompositionNode>();
        return new WebCompositionSlotNode(RequiredString(value, "name", path, 1, 100), fallback);
    }

    private static WebCompositionNode ParseComponentReference(JsonElement value, string path, ISet<string> assets, ParseBudget budget, int depth)
    {
        RejectUnknown(value, path, "kind", "id", "revision", "props", "slots");
        var props = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (value.TryGetProperty("props", out var propValues))
        {
            RequireObject(propValues, path + ".props", "PROPS_OBJECT_REQUIRED");
            foreach (var prop in propValues.EnumerateObject())
            {
                if (!IsIdentifier(prop.Name)) throw Error(path + ".props." + prop.Name, "INVALID_PROPERTY_NAME", "Property names must be identifiers.");
                ValidateSerializable(prop.Value, path + ".props." + prop.Name, 0);
                props.Add(prop.Name, prop.Value.Clone());
            }
        }
        var slots = new Dictionary<string, IReadOnlyList<WebCompositionNode>>(StringComparer.Ordinal);
        if (value.TryGetProperty("slots", out var slotValues))
        {
            RequireObject(slotValues, path + ".slots", "SLOTS_OBJECT_REQUIRED");
            foreach (var slot in slotValues.EnumerateObject())
            {
                if (!IsIdentifier(slot.Name)) throw Error(path + ".slots." + slot.Name, "INVALID_SLOT_NAME", "Slot names must be identifiers.");
                slots.Add(slot.Name, ParseNodeArray(slot.Value, path + ".slots." + slot.Name, assets, budget, depth + 1));
            }
        }
        return new WebCompositionComponentNode(new(
            RequiredString(value, "id", path, 1, 100), RequiredString(value, "revision", path, 1, 100)),
            new ReadOnlyDictionary<string, JsonElement>(props),
            new ReadOnlyDictionary<string, IReadOnlyList<WebCompositionNode>>(slots));
    }

    private static IReadOnlyList<WebCompositionNode> ParseChildren(JsonElement source, string name, string path, ISet<string> assets, ParseBudget budget, int depth) =>
        ParseNodeArray(Required(source, name, path, JsonValueKind.Array), path + "." + name, assets, budget, depth + 1);

    private static IReadOnlyList<WebCompositionNode> ParseNodeArray(JsonElement values, string path, ISet<string> assets, ParseBudget budget, int depth)
    {
        var result = new List<WebCompositionNode>();
        var index = 0;
        foreach (var child in values.EnumerateArray())
            result.Add(ParseNode(child, $"{path}[{index++}]", assets, budget, depth));
        return result.AsReadOnly();
    }

    private static void ValidateReferences(WebCompositionDocument document, ParseBudget budget)
    {
        var graph = document.Components.Keys.ToDictionary(key => key, _ => new HashSet<WebCompositionComponentKey>());
        ValidateNode(document.Root, "$.root", document, graph, null, budget, EmptyNames());
        foreach (var component in document.Components.Values)
            ValidateNode(component.Template, "$.components." + component.Key + ".template", document, graph, component.Key, budget, EmptyNames());
        var visiting = new HashSet<WebCompositionComponentKey>();
        var visited = new HashSet<WebCompositionComponentKey>();
        foreach (var component in graph.Keys) Visit(component);
        return;

        void Visit(WebCompositionComponentKey component)
        {
            if (visited.Contains(component)) return;
            if (!visiting.Add(component)) throw Error("$.components", "COMPONENT_CYCLE", $"Component cycle includes '{component}'.");
            foreach (var next in graph[component]) Visit(next);
            visiting.Remove(component);
            visited.Add(component);
        }
    }

    private static void ValidateNode(WebCompositionNode node, string path, WebCompositionDocument document,
        IDictionary<WebCompositionComponentKey, HashSet<WebCompositionComponentKey>> graph,
        WebCompositionComponentKey? owner, ParseBudget budget, IReadOnlySet<string> locals)
    {
        switch (node)
        {
            case WebCompositionComponentNode component:
                if (!document.Components.TryGetValue(component.Component, out var definition))
                    throw Error(path, "MISSING_COMPONENT", $"Component '{component.Component}' is not in this generation.");
                foreach (var required in definition.RequiredProps)
                    if (!component.Props.ContainsKey(required))
                        throw Error(path + ".props", "MISSING_REQUIRED_PROP", $"Component '{component.Component}' requires '{required}'.");
                if (owner is not null) graph[owner].Add(component.Component);
                foreach (var slot in component.Slots)
                    foreach (var child in slot.Value)
                        ValidateNode(child, path + ".slots." + slot.Key, document, graph, owner, budget, locals);
                break;
            case WebCompositionElementNode element:
                if (element.Action is not null && !document.ActionBindings.ContainsKey(element.Action))
                    throw Error(path + ".action", "MISSING_ACTION_BINDING", $"Action '{element.Action}' is not declared.");
                foreach (var child in element.Children) ValidateNode(child, path + ".children", document, graph, owner, budget, locals);
                break;
            case WebCompositionIfNode conditional:
                ValidatePath(conditional.Condition, path + ".condition", document, locals);
                foreach (var child in conditional.Children.Concat(conditional.Otherwise)) ValidateNode(child, path + ".children", document, graph, owner, budget, locals);
                break;
            case WebCompositionEachNode loop:
                ValidatePath(loop.Items, path + ".items", document, locals);
                if (loop.ItemName == "props" || document.QueryBindings.ContainsKey(loop.ItemName) || locals.Contains(loop.ItemName))
                    throw Error(path + ".as", "RESERVED_LOOP_NAME", "A loop name cannot shadow props, a query binding, or another local value.");
                var loopLocals = new HashSet<string>(locals, StringComparer.Ordinal) { loop.ItemName };
                foreach (var child in loop.Children) ValidateNode(child, path + ".children", document, graph, owner, budget, new ReadOnlySet<string>(loopLocals));
                break;
            case WebCompositionValueNode value:
                ValidatePath(value.Path, path + ".path", document, locals);
                break;
            case WebCompositionSlotNode slot:
                foreach (var child in slot.Fallback) ValidateNode(child, path + ".fallback", document, graph, owner, budget, locals);
                break;
        }
    }

    private static void ValidatePath(string value, string path, WebCompositionDocument document, IReadOnlySet<string> locals)
    {
        var root = value.Split('.', 2)[0];
        if (!locals.Contains(root) && !document.QueryBindings.ContainsKey(root) && root != "props")
            throw Error(path, "UNKNOWN_VALUE_ROOT", $"Value root '{root}' is not a declared query or local value.");
    }

    private static void ValidateAssetReference(string value, ISet<string> availableAssets, string path)
    {
        if (!value.StartsWith("asset:", StringComparison.Ordinal)) return;
        var asset = value[6..];
        if (!WebPageAssetPath.TryValidate(asset, out _) || !asset.StartsWith("assets/", StringComparison.Ordinal) || !availableAssets.Contains(asset))
            throw Error(path, "MISSING_ASSET", "The asset must be an exact path supplied by the selected content revision.");
    }

    private static void ValidateAttributeValue(string tag, string name, string value, string path, ISet<string> assets)
    {
        if (value.Any(char.IsControl) || value.Contains('\\', StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
            throw Error(path, "UNSAFE_ATTRIBUTE_VALUE", "The attribute value is not safe.");
        if (name == "href")
        {
            if (tag != "a" || (!value.StartsWith('#') && !value.StartsWith("asset:", StringComparison.Ordinal)))
                throw Error(path, "UNSAFE_ATTRIBUTE_VALUE", "Links must be same-page fragments or selected-revision assets.");
        }
        if (name == "src")
        {
            if (tag != "img" || !value.StartsWith("asset:", StringComparison.Ordinal))
                throw Error(path, "UNSAFE_ATTRIBUTE_VALUE", "Image sources must be selected-revision assets.");
        }
        if (value.StartsWith("asset:", StringComparison.Ordinal))
            ValidateAssetReference(value, assets, path);
    }

    private static void ValidateSerializable(JsonElement value, string path, int depth)
    {
        if (depth > WebComposition.MaximumDepth) throw Error(path, "VALUE_DEPTH_EXCEEDED", "Property data exceeds its depth limit.");
        if (value.ValueKind is JsonValueKind.Undefined) throw Error(path, "UNSERIALIZABLE_PROP", "Properties must be JSON values.");
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var child in value.EnumerateObject()) ValidateSerializable(child.Value, path + "." + child.Name, depth + 1);
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) ValidateSerializable(child, path, depth + 1);
    }

    private static JsonElement Required(JsonElement source, string name, string path, JsonValueKind kind)
    {
        if (!source.TryGetProperty(name, out var value)) throw Error(path + "." + name, "MISSING_FIELD", "A required field is missing.");
        if (value.ValueKind != kind) throw Error(path + "." + name, "INVALID_FIELD_TYPE", $"The field must be {kind}.");
        return value;
    }

    private static string RequiredString(JsonElement source, string name, string path, int min, int max)
    {
        var value = Required(source, name, path, JsonValueKind.String).GetString()!;
        if (value.Length < min || value.Length > max || value.Any(char.IsControl))
            throw Error(path + "." + name, "INVALID_STRING", "The string is outside its allowed bounds.");
        return value;
    }

    private static string RequiredPath(JsonElement source, string name, string path)
    {
        var value = RequiredString(source, name, path, 1, 300);
        if (value.Split('.').Any(part => !IsIdentifier(part)))
            throw Error(path + "." + name, "INVALID_VALUE_PATH", "Value paths use dot-separated identifiers.");
        return value;
    }

    private static bool IsIdentifier(string value) => value.Length <= 80 && value.Length > 0 &&
        ((value[0] is >= 'A' and <= 'Z') || (value[0] is >= 'a' and <= 'z') || value[0] == '_') &&
        value.All(character => (character is >= 'A' and <= 'Z') || (character is >= 'a' and <= 'z') ||
            (character is >= '0' and <= '9') || character == '_');

    private static void RequireObject(JsonElement value, string path, string code)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error(path, code, "The value must be an object.");
    }

    private static void RejectUnknown(JsonElement value, string path, params string[] allowed)
    {
        var fields = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!fields.Contains(property.Name)) throw Error(path + "." + property.Name, "UNKNOWN_FIELD", "The field is not allowed in this format version.");
    }

    private static void EnsureNoDuplicateProperties(JsonElement value, string path, int depth)
    {
        if (depth > WebComposition.MaximumDepth) throw Error(path, "DEPTH_LIMIT_EXCEEDED", "The JSON depth limit is 32.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Error(path + "." + property.Name, "DUPLICATE_FIELD", "JSON object fields must be unique.");
                EnsureNoDuplicateProperties(property.Value, path + "." + property.Name, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) EnsureNoDuplicateProperties(item, $"{path}[{index++}]", depth + 1);
        }
    }

    private static CompositionException Error(string path, string code, string message) => new(path, code, message);
    private static IReadOnlySet<string> EmptyNames() => new ReadOnlySet<string>(new HashSet<string>(StringComparer.Ordinal));
    private sealed class ParseBudget { public int Nodes { get; set; } }
    private sealed class CompositionException(string path, string code, string message) : Exception(message)
    {
        public string Path { get; } = path;
        public string Code { get; } = code;
    }
}

public sealed class WebCompositionRenderer
{
    public WebCompositionRenderResult Render(WebCompositionDocument document, IReadOnlyDictionary<string, JsonElement> queryValues,
        string? assetBasePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(queryValues);
        var errors = new List<WebCompositionError>();
        foreach (var binding in document.QueryBindings.Keys)
            if (!queryValues.ContainsKey(binding)) errors.Add(new("$.queries." + binding, "MISSING_QUERY_DATA", "The declared query has no materialized value."));
        if (errors.Count != 0) return new(null, errors.AsReadOnly());
        try
        {
            var output = new BoundedHtmlWriter();
            RenderNode(document.Root, new RenderScope(queryValues, EmptyJsonObject(), new Dictionary<string, IReadOnlyList<WebCompositionNode>>(), null, null, assetBasePath), document, output, 0, new RenderBudget());
            return new(output.ToString(), errors.AsReadOnly());
        }
        catch (RenderException exception)
        {
            errors.Add(new(exception.Path, exception.Code, exception.Message));
            return new(null, errors.AsReadOnly());
        }
    }

    private static void RenderNode(WebCompositionNode node, RenderScope scope, WebCompositionDocument document, BoundedHtmlWriter output, int depth, RenderBudget budget)
    {
        if (depth > WebComposition.MaximumDepth) throw Error("$", "RENDER_DEPTH_EXCEEDED", "The render depth limit was exceeded.");
        if (++budget.Nodes > WebComposition.MaximumExpandedRenderNodes)
            throw Error("$", "RENDER_NODE_LIMIT_EXCEEDED", "Expanded rendering exceeds its node budget.");
        switch (node)
        {
            case WebCompositionTextNode text: AppendEscaped(output, text.Text); break;
            case WebCompositionValueNode value: AppendEscaped(output, Display(Resolve(value.Path, scope))); break;
            case WebCompositionAssetNode asset: AppendEscaped(output, ResolveAssetPath(asset.Path, scope.AssetBasePath)); break;
            case WebCompositionElementNode element:
                output.Append('<').Append(element.Tag);
                foreach (var attribute in element.Attributes.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    output.Append(' ').Append(attribute.Key).Append("=\"");
                    AppendEscaped(output, attribute.Value.StartsWith("asset:", StringComparison.Ordinal)
                        ? ResolveAssetPath(attribute.Value[6..], scope.AssetBasePath) : attribute.Value);
                    output.Append('"');
                }
                if (element.Action is not null)
                {
                    output.Append(" data-web-action=\""); AppendEscaped(output, element.Action);
                    output.Append("\" type=\"button\" disabled aria-disabled=\"true\"");
                }
                else if (element.Tag == "button") output.Append(" type=\"button\"");
                output.Append('>');
                if (element.Tag != "img")
                {
                    RenderNodes(element.Children, scope, document, output, depth + 1, budget);
                    output.Append("</").Append(element.Tag).Append('>');
                }
                break;
            case WebCompositionIfNode conditional:
                RenderNodes(IsTruthy(Resolve(conditional.Condition, scope)) ? conditional.Children : conditional.Otherwise, scope, document, output, depth + 1, budget);
                break;
            case WebCompositionEachNode loop:
                var source = Resolve(loop.Items, scope);
                if (source.ValueKind != JsonValueKind.Array) throw Error("$", "LOOP_VALUE_NOT_ARRAY", "A loop value must be an array.");
                if (source.GetArrayLength() > WebComposition.MaximumLoopItems) throw Error("$", "LOOP_LIMIT_EXCEEDED", "A loop exceeds the 100-item render limit.");
                foreach (var item in source.EnumerateArray())
                {
                    var locals = new Dictionary<string, JsonElement>(scope.Locals ?? EmptyLocals(), StringComparer.Ordinal) { [loop.ItemName] = item.Clone() };
                    RenderNodes(loop.Children, scope with { Locals = locals }, document, output, depth + 1, budget);
                }
                break;
            case WebCompositionSlotNode slot:
                if (scope.Slots.TryGetValue(slot.Name, out var content)) RenderNodes(content, scope.SlotCaller ?? scope, document, output, depth + 1, budget);
                else RenderNodes(slot.Fallback, scope, document, output, depth + 1, budget);
                break;
            case WebCompositionComponentNode component:
                if (!document.Components.TryGetValue(component.Component, out var definition)) throw Error("$", "MISSING_COMPONENT", "The component is not available.");
                RenderNode(definition.Template, new(scope.Queries, Props(component.Props), component.Slots, scope.Locals, scope, scope.AssetBasePath), document, output, depth + 1, budget);
                break;
        }
    }

    private static void RenderNodes(IEnumerable<WebCompositionNode> nodes, RenderScope scope, WebCompositionDocument document, BoundedHtmlWriter output, int depth, RenderBudget budget)
    {
        foreach (var node in nodes) RenderNode(node, scope, document, output, depth, budget);
    }

    private static JsonElement Resolve(string path, RenderScope scope)
    {
        var parts = path.Split('.');
        JsonElement value;
        if (parts[0] == "props") value = scope.Props;
        else if (scope.Locals is not null && scope.Locals.TryGetValue(parts[0], out var local)) value = local;
        else if (!scope.Queries.TryGetValue(parts[0], out value)) throw Error("$", "MISSING_VALUE", $"'{parts[0]}' has no render value.");
        foreach (var part in parts.Skip(1))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
                throw Error("$", "MISSING_VALUE", $"'{path}' has no render value.");
        }
        return value;
    }

    private static bool IsTruthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False or JsonValueKind.Null => false,
        JsonValueKind.String => !string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() != 0,
        JsonValueKind.Object => true,
        JsonValueKind.Number => value.TryGetDecimal(out var number) && number != 0,
        _ => false
    };

    private static string Display(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText()
    };

    private static void AppendEscaped(BoundedHtmlWriter output, string value) => HtmlEncoder.Default.Encode(output, value);

    // Both markup and the HTML encoder write through this cap. Never build an entire expanded
    // escaped string first: a small input can expand several times before a node-level check.
    private sealed class BoundedHtmlWriter : TextWriter
    {
        private readonly StringBuilder buffer = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            RequireSpace(1);
            buffer.Append(value);
        }
        public override void Write(string? value)
        {
            if (value is null) return;
            Write(value.AsSpan());
        }
        public override void Write(char[] value, int index, int count) => Write(value.AsSpan(index, count));
        public override void Write(ReadOnlySpan<char> value)
        {
            RequireSpace(value.Length);
            buffer.Append(value);
        }
        public BoundedHtmlWriter Append(char value) { Write(value); return this; }
        public BoundedHtmlWriter Append(string value) { Write(value); return this; }
        public override string ToString() => buffer.ToString();
        private void RequireSpace(int count)
        {
            if (count > WebComposition.MaximumRenderedCharacters - buffer.Length)
                throw Error("$", "RENDER_OUTPUT_LIMIT_EXCEEDED", "Rendered output exceeds its character budget.");
        }
    }

    private static string ResolveAssetPath(string assetPath, string? assetBasePath)
    {
        if (!TryValidateAssetBasePath(assetBasePath, out var validated))
            throw Error("$", assetBasePath is null ? "ASSET_BASE_REQUIRED" : "UNSAFE_ASSET_BASE",
                "Asset rendering requires a safe host-supplied page asset route base.");
        return validated + assetPath;
    }

    private static bool TryValidateAssetBasePath(string? value, out string validated)
    {
        validated = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains('?', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal) ||
            !value.StartsWith("/ui/", StringComparison.Ordinal) || !value.EndsWith("/", StringComparison.Ordinal)) return false;
        var parts = value[4..^1].Split('/');
        var legacy = parts.Length == 1 && WebPageId.IsValid(parts[0]);
        var pinned = parts.Length == 5 && WebPageId.IsValid(parts[0]) && parts[1] == "content" &&
            WebPageId.IsValid(parts[2]) && parts[3] == "revisions" &&
            int.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var revision) &&
            revision > 0 && parts[4] == revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!legacy && !pinned) return false;
        validated = value;
        return true;
    }
    private static JsonElement Props(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.SerializeToElement(values);
    private static JsonElement EmptyJsonObject() => JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    private static IReadOnlyDictionary<string, JsonElement> EmptyLocals() => new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    private static RenderException Error(string path, string code, string message) => new(path, code, message);
    private sealed record RenderScope(IReadOnlyDictionary<string, JsonElement> Queries, JsonElement Props,
        IReadOnlyDictionary<string, IReadOnlyList<WebCompositionNode>> Slots, IReadOnlyDictionary<string, JsonElement>? Locals,
        RenderScope? SlotCaller, string? AssetBasePath);
    private sealed class RenderBudget { public int Nodes { get; set; } }
    private sealed class RenderException(string path, string code, string message) : Exception(message)
    { public string Path { get; } = path; public string Code { get; } = code; }
}
