using System.Text.Json;

namespace DantesRoleplay.Web.Settings;

public enum HostSettingSensitivity
{
    PublicValue,
    ConfiguredOnly
}

public enum HostSettingMutability
{
    ReadOnly,
    Live,
    RestartRequired
}

public enum HostSettingDisruption
{
    None,
    LocalCompletion,
    HostRestart
}

public enum HostSettingRuntimeState
{
    Ready,
    NotRegistered,
    Unavailable
}

public sealed record HostSettingRuntime(
    HostSettingRuntimeState State,
    string Message);

public sealed record HostSettingDefinition(
    string Key,
    string DisplayName,
    string Description,
    HostSettingSensitivity Sensitivity,
    HostSettingMutability Mutability,
    HostSettingDisruption Disruption,
    string Source,
    bool Configured,
    JsonElement? Value,
    JsonElement? EffectiveValue,
    JsonElement? PendingValue,
    bool RestartRequired,
    JsonElement Schema);

public sealed record HostSettingCatalog(
    HostSettingRuntime Runtime,
    IReadOnlyList<HostSettingDefinition> Definitions);

public interface IHostSettingDefinitionProvider
{
    HostSettingCatalog GetCatalog();
    JsonElement NormalizeOverride(string key, JsonElement value);
    void ApplyStartupOverrides(IReadOnlyDictionary<string, JsonElement?> overrides);
}

internal sealed class UnavailableHostSettingDefinitionProvider : IHostSettingDefinitionProvider
{
    public static UnavailableHostSettingDefinitionProvider Instance { get; } = new();

    private static readonly HostSettingCatalog Catalog = new(
        new(HostSettingRuntimeState.Unavailable, "Host setting definitions are unavailable."),
        []);

    public HostSettingCatalog GetCatalog() => Catalog;

    public JsonElement NormalizeOverride(string key, JsonElement value) =>
        throw new KeyNotFoundException($"Host setting '{key}' is unavailable.");

    public void ApplyStartupOverrides(IReadOnlyDictionary<string, JsonElement?> overrides)
    {
        if (overrides.Count != 0)
            throw new InvalidOperationException("Host setting overrides cannot be applied without a host provider.");
    }
}
