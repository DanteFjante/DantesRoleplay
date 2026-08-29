using System.Globalization;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Interactions;
using Microsoft.Extensions.Configuration;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Closed startup-only configuration for the application-facing outer provider. It intentionally
/// does not participate in player input, MCP requests, or mutable host-setting overrides.
/// </summary>
public sealed class InteractionOuterHostOptions
{
    private const string Section = "InteractionOuter";

    public InteractionOuterHostOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Selection = new() { Provider = ParseProvider(configuration[$"{Section}:Provider"] ?? "local") };

        var defaults = new OllamaCompletionOptions { Profile = "outer" };
        var local = new OllamaCompletionOptions
        {
            Enabled = ReadBoolean(configuration, "Local:Enabled", defaults.Enabled),
            Endpoint = ReadUri(configuration, "Local:Endpoint", defaults.Endpoint),
            Model = ReadString(configuration, "Local:Model", defaults.Model),
            Profile = ReadString(configuration, "Local:Profile", defaults.Profile),
            MaxPromptCharacters = ReadInt32(configuration, "Local:MaxPromptCharacters", defaults.MaxPromptCharacters),
            MaxResponseCharacters = ReadInt32(configuration, "Local:MaxResponseCharacters", defaults.MaxResponseCharacters),
            MaxOutputTokens = ReadInt32(configuration, "Local:MaxOutputTokens", defaults.MaxOutputTokens),
            MaxConcurrentRequests = ReadInt32(configuration, "Local:MaxConcurrentRequests", defaults.MaxConcurrentRequests),
            Timeout = ReadTimeSpan(configuration, "Local:Timeout", defaults.Timeout),
            ReadinessCache = defaults.ReadinessCache,
            KeepAlive = defaults.KeepAlive,
            AllowedTaskClasses = new HashSet<string>(StringComparer.Ordinal)
            {
                InteractionOuterProtocol.OuterTurnTask,
                InteractionOuterProtocol.NarrationTask,
                InteractionOuterProtocol.TaskAgendaTask,
                InteractionPlannerProtocol.TaskClass
            }
        };
        var error = local.Validate();
        if (error is not null)
            throw new InvalidOperationException($"The InteractionOuter local provider configuration is invalid: {error}");
        LocalCompletion = local;
        LocalAdapter = new()
        {
            Model = local.Model,
            Profile = local.Profile,
            MaximumOutputBytes = Math.Min(local.MaxResponseCharacters, InteractionContractLimits.JsonBytes)
        };
        error = LocalAdapter.Validate();
        if (error is not null)
            throw new InvalidOperationException($"The InteractionOuter local adapter configuration is invalid: {error}");
    }

    public InteractionOuterProviderSelectionOptions Selection { get; }
    public OllamaCompletionOptions LocalCompletion { get; }
    public LocalInteractionOuterProviderOptions LocalAdapter { get; }

    private static InteractionOuterProviderKind ParseProvider(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "local" => InteractionOuterProviderKind.Local,
        "remote" => InteractionOuterProviderKind.Remote,
        _ => throw new InvalidOperationException("InteractionOuter:Provider must be 'local' or 'remote'.")
    };

    private static bool ReadBoolean(IConfiguration configuration, string name, bool fallback)
    {
        var raw = configuration[Key(name)];
        return raw is null ? fallback : bool.TryParse(raw, out var value)
            ? value : throw Invalid(name, "The value must be true or false.");
    }

    private static int ReadInt32(IConfiguration configuration, string name, int fallback)
    {
        var raw = configuration[Key(name)];
        return raw is null ? fallback : int.TryParse(raw, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value)
            ? value : throw Invalid(name, "The value must be a base-10 integer.");
    }

    private static string ReadString(IConfiguration configuration, string name, string fallback) =>
        configuration[Key(name)] ?? fallback;

    private static Uri ReadUri(IConfiguration configuration, string name, Uri fallback)
    {
        var raw = configuration[Key(name)];
        return raw is null ? fallback : Uri.TryCreate(raw, UriKind.Absolute, out var value)
            ? value : throw Invalid(name, "The value must be an absolute URI.");
    }

    private static TimeSpan ReadTimeSpan(IConfiguration configuration, string name, TimeSpan fallback)
    {
        var raw = configuration[Key(name)];
        return raw is null ? fallback : TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value)
            ? value : throw Invalid(name, "The value must be a TimeSpan.");
    }

    private static string Key(string name) => $"{Section}:{name}";
    private static InvalidOperationException Invalid(string name, string message) =>
        new($"InteractionOuter setting '{name}' is invalid. {message}");
}
