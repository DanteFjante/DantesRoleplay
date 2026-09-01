using System.Globalization;
using System.Text.Json;
using DantesRoleplay.AI.Ollama;
using DantesRoleplay.Web.Settings;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemConversations;
using Microsoft.Extensions.Configuration;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Host-owned, immutable view of the closed local-completion startup setting allowlist.
/// It deliberately does not activate or refresh the optional provider.
/// </summary>
public sealed class ConfiguredHostSettingDefinitionProvider : IHostSettingDefinitionProvider
{
    private const string Section = "Knowledge:Completion";
    private readonly HostSettingCatalog baseCatalog;
    private HostSettingCatalog catalog;

    public ConfiguredHostSettingDefinitionProvider(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var defaults = new OllamaCompletionOptions();
        var enabled = ReadBoolean(configuration, "Enabled", defaults.Enabled);
        var endpoint = ReadUri(configuration, "Endpoint", defaults.Endpoint);
        var model = ReadString(configuration, "Model", defaults.Model);
        var profile = ReadString(configuration, "Profile", defaults.Profile);
        var maxOutputTokens = ReadInt32(configuration, "MaxOutputTokens", defaults.MaxOutputTokens);
        var timeout = ReadTimeSpan(configuration, "Timeout", defaults.Timeout);
        var maxConcurrentRequests = ReadInt32(
            configuration, "MaxConcurrentRequests", defaults.MaxConcurrentRequests);

        if (timeout.Value.Ticks % TimeSpan.TicksPerSecond != 0)
            throw Invalid("local-completion.timeout-seconds", "Timeout must resolve to whole seconds.");

        var options = new OllamaCompletionOptions
        {
            Enabled = enabled.Value,
            Endpoint = endpoint.Value,
            Model = model.Value,
            Profile = profile.Value,
            MaxPromptCharacters = defaults.MaxPromptCharacters,
            MaxResponseCharacters = defaults.MaxResponseCharacters,
            MaxOutputTokens = maxOutputTokens.Value,
            MaxConcurrentRequests = maxConcurrentRequests.Value,
            Timeout = timeout.Value,
            ReadinessCache = defaults.ReadinessCache,
            KeepAlive = defaults.KeepAlive
        };
        var validationError = options.ValidateProviderSettings();
        if (validationError is not null)
            throw new InvalidOperationException($"The local-completion host settings are invalid: {validationError}");

        baseCatalog = new(
            new(
                HostSettingRuntimeState.NotRegistered,
                "Local completion startup settings are resolved, but the provider is not registered in this host."),
            Array.AsReadOnly<HostSettingDefinition>(
            [
                Definition(
                    "local-completion.enabled",
                    "Local completion enabled",
                    "Whether startup configuration requests the optional local completion provider.",
                    enabled,
                    Schema("""{"type":"boolean","default":false}""")),
                Definition(
                    "local-completion.endpoint",
                    "Ollama endpoint",
                    "The absolute loopback HTTP or HTTPS endpoint reserved for local completion.",
                    Project(endpoint, value => value.ToString()),
                    Schema("""{"type":"string","format":"uri","x-loopbackOnly":true,"default":"http://localhost:11434/"}""")),
                Definition(
                    "local-completion.model",
                    "Model",
                    "The configured Ollama completion model name.",
                    model,
                    Schema("""{"type":"string","minLength":1,"maxLength":200,"default":"qwen3:8b"}""")),
                Definition(
                    "local-completion.profile",
                    "Profile",
                    "The trimmed host-selected local completion profile.",
                    profile,
                    Schema("""{"type":"string","minLength":1,"maxLength":100,"pattern":"^\\S(?:.*\\S)?$","default":"standard"}""")),
                Definition(
                    "local-completion.max-output-tokens",
                    "Maximum output tokens",
                    "The maximum number of tokens accepted from one local completion.",
                    maxOutputTokens,
                    Schema("""{"type":"integer","minimum":64,"maximum":8192,"default":1024}""")),
                Definition(
                    "local-completion.timeout-seconds",
                    "Timeout seconds",
                    "The maximum whole seconds allowed for one local completion request.",
                    Project(timeout, value => checked((int)value.TotalSeconds)),
                    Schema("""{"type":"integer","minimum":1,"maximum":600,"default":90}""")),
                Definition(
                    "local-completion.max-concurrent-requests",
                    "Maximum concurrent requests",
                    "The maximum local completion requests admitted concurrently.",
                    maxConcurrentRequests,
                    Schema("""{"type":"integer","minimum":1,"maximum":8,"default":1}"""))
            ]));
        catalog = baseCatalog;
    }

    public HostSettingCatalog GetCatalog() => catalog;

    public OllamaCompletionOptions CreateCompletionOptions() => new()
    {
        Enabled = Value("local-completion.enabled").GetBoolean(),
        Endpoint = new Uri(Value("local-completion.endpoint").GetString()!, UriKind.Absolute),
        Model = Value("local-completion.model").GetString()!,
        Profile = Value("local-completion.profile").GetString()!,
        MaxOutputTokens = Value("local-completion.max-output-tokens").GetInt32(),
        Timeout = TimeSpan.FromSeconds(Value("local-completion.timeout-seconds").GetInt32()),
        MaxConcurrentRequests = Value("local-completion.max-concurrent-requests").GetInt32(),
        AllowedTaskClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            AssistantConversationService.TaskClass,
            SystemConversationService.TaskClass,
            InteractionPlannerProtocol.TaskClass
        }
    };

    public void MarkProviderRegistered()
    {
        catalog = new(
            new(HostSettingRuntimeState.Ready,
                "Local completion is registered; assistant readiness is reported separately."),
            Array.AsReadOnly(catalog.Definitions.Select(definition => definition with
            {
                EffectiveValue = definition.Value?.Clone()
            }).ToArray()));
    }

    public JsonElement NormalizeOverride(string key, JsonElement value)
    {
        if (!baseCatalog.Definitions.Any(definition => definition.Key == key))
            throw new KeyNotFoundException($"Host setting '{key}' is not registered.");

        return key switch
        {
            "local-completion.enabled" when value.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                JsonSerializer.SerializeToElement(value.GetBoolean()),
            "local-completion.endpoint" when value.ValueKind is JsonValueKind.String =>
                JsonSerializer.SerializeToElement(NormalizeEndpoint(value.GetString()!)),
            "local-completion.model" when value.ValueKind is JsonValueKind.String =>
                JsonSerializer.SerializeToElement(NormalizeString(key, value.GetString()!, 200)),
            "local-completion.profile" when value.ValueKind is JsonValueKind.String =>
                JsonSerializer.SerializeToElement(NormalizeString(key, value.GetString()!, 100)),
            "local-completion.max-output-tokens" => NormalizeInteger(key, value, 64, 8192),
            "local-completion.timeout-seconds" => NormalizeInteger(key, value, 1, 600),
            "local-completion.max-concurrent-requests" => NormalizeInteger(key, value, 1, 8),
            _ => throw Invalid(key, "The JSON value has the wrong type.")
        };
    }

    public void ApplyStartupOverrides(IReadOnlyDictionary<string, JsonElement?> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        foreach (var key in overrides.Keys)
            if (!baseCatalog.Definitions.Any(definition => definition.Key == key))
                throw new InvalidOperationException($"Stored host setting '{key}' is not registered by this host.");

        var definitions = baseCatalog.Definitions.Select(definition =>
        {
            if (!overrides.TryGetValue(definition.Key, out var value) || value is null)
                return definition;
            var normalized = NormalizeOverride(definition.Key, value.Value);
            return definition with
            {
                Source = "override",
                Configured = true,
                Value = normalized,
                PendingValue = null,
                RestartRequired = false
            };
        }).ToArray();
        catalog = new(baseCatalog.Runtime, Array.AsReadOnly(definitions));
    }

    private JsonElement Value(string key) => catalog.Definitions.Single(item => item.Key == key).Value!.Value;

    private static string NormalizeEndpoint(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.HostNameType is UriHostNameType.Unknown ||
            !endpoint.IsLoopback ||
            endpoint.Scheme is not ("http" or "https"))
            throw Invalid("local-completion.endpoint", "The endpoint must be an absolute loopback HTTP or HTTPS URI.");
        return endpoint.ToString();
    }

    private static string NormalizeString(string key, string raw, int maximum)
    {
        var value = raw.Trim();
        if (value.Length is 0 || value.Length > maximum)
            throw Invalid(key, $"The value must contain 1 to {maximum} characters after trimming.");
        return value;
    }

    private static JsonElement NormalizeInteger(string key, JsonElement value, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) ||
            number < minimum || number > maximum)
            throw Invalid(key, $"The value must be an integer from {minimum} through {maximum}.");
        return JsonSerializer.SerializeToElement(number);
    }

    private static HostSettingDefinition Definition<T>(
        string key,
        string displayName,
        string description,
        Resolved<T> resolved,
        JsonElement schema) =>
        new(
            key,
            displayName,
            description,
            HostSettingSensitivity.PublicValue,
            HostSettingMutability.RestartRequired,
            HostSettingDisruption.HostRestart,
            resolved.Configured ? "configuration" : "default",
            resolved.Configured,
            JsonSerializer.SerializeToElement(resolved.Value),
            null,
            null,
            false,
            schema);

    private static Resolved<TResult> Project<T, TResult>(Resolved<T> source, Func<T, TResult> project) =>
        new(project(source.Value), source.Configured);

    private static Resolved<bool> ReadBoolean(IConfiguration configuration, string name, bool fallback)
    {
        var raw = configuration[Path(name)];
        if (raw is null) return new(fallback, false);
        if (!bool.TryParse(raw, out var value)) throw Invalid(Key(name), "The value must be true or false.");
        return new(value, true);
    }

    private static Resolved<int> ReadInt32(IConfiguration configuration, string name, int fallback)
    {
        var raw = configuration[Path(name)];
        if (raw is null) return new(fallback, false);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw Invalid(Key(name), "The value must be a base-10 integer.");
        return new(value, true);
    }

    private static Resolved<string> ReadString(IConfiguration configuration, string name, string fallback)
    {
        var value = configuration[Path(name)];
        return value is null ? new(fallback, false) : new(value, true);
    }

    private static Resolved<Uri> ReadUri(IConfiguration configuration, string name, Uri fallback)
    {
        var raw = configuration[Path(name)];
        if (raw is null) return new(fallback, false);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var value))
            throw Invalid(Key(name), "The value must be an absolute URI.");
        return new(value, true);
    }

    private static Resolved<TimeSpan> ReadTimeSpan(
        IConfiguration configuration, string name, TimeSpan fallback)
    {
        var raw = configuration[Path(name)];
        if (raw is null) return new(fallback, false);
        if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value))
            throw Invalid(Key(name), "The value must be a TimeSpan.");
        return new(value, true);
    }

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    private static string Path(string name) => $"{Section}:{name}";
    private static string Key(string name) => name switch
    {
        "Enabled" => "local-completion.enabled",
        "Endpoint" => "local-completion.endpoint",
        "Model" => "local-completion.model",
        "Profile" => "local-completion.profile",
        "MaxOutputTokens" => "local-completion.max-output-tokens",
        "Timeout" => "local-completion.timeout-seconds",
        "MaxConcurrentRequests" => "local-completion.max-concurrent-requests",
        _ => "local-completion"
    };

    private static InvalidOperationException Invalid(string key, string message) =>
        new($"Host setting '{key}' is invalid. {message}");

    private readonly record struct Resolved<T>(T Value, bool Configured);
}
