using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using DantesRoleplay.HostSettings;
using DantesRoleplay.Web.Settings;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

public sealed record ControlSettingPage(string State, string Message, IReadOnlyList<ControlSettingSummary> Items);

public sealed record ControlSettingSummary(
    string Key, string DisplayName, string Description, string Sensitivity, string Mutability,
    string Disruption, string Source, bool Configured, string RuntimeState, JsonElement? Value,
    JsonElement? EffectiveValue, JsonElement? PendingValue, bool RestartRequired,
    int Revision, int AppliedRevision);

public sealed record ControlSettingDetail(ControlSettingSummary Summary, JsonElement Schema);
public sealed record ControlSettingUpdateRequest(int? ExpectedRevision, JsonElement Value);
public sealed record ControlSettingResetRequest(int? ExpectedRevision);
public sealed record ControlSettingRollbackRequest(int? ExpectedRevision, int? TargetRevision);
public sealed record ControlSettingWriteResult(int Revision, int AppliedRevision, string OperationId, bool RestartRequired);
public sealed record ControlSettingVersionPage(IReadOnlyList<ControlSettingVersionItem> Items, int? NextBeforeVersion);
public sealed record ControlSettingVersionItem(
    int Version, string State, DateTime CreatedAtUtc, string CreatedBy, string OperationId,
    bool IsReset, JsonElement? Value);

public sealed class ControlSettingsException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Bounded web adapter over host-owned definitions and the generic override store.</summary>
public sealed partial class ControlSettingsExplorer
{
    public const int MaximumDefinitions = 32;
    private const int MaximumJsonBodyBytes = 16 * 1024;
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly IHostSettingDefinitionProvider provider;
    private readonly IHostSettingOverrideStore? store;

    public ControlSettingsExplorer(IHostSettingDefinitionProvider provider, IHostSettingOverrideStore? store = null)
    {
        this.provider = provider;
        this.store = store;
    }

    public static async Task<T> ReadBodyAsync<T>(HttpRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ContentLength > MaximumJsonBodyBytes)
            throw new ControlSettingsException("BODY_TOO_LARGE", "The settings request exceeds 16 KiB.", StatusCodes.Status413PayloadTooLarge);
        await using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumJsonBodyBytes)
                throw new ControlSettingsException("BODY_TOO_LARGE", "The settings request exceeds 16 KiB.", StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0) throw InvalidValue("A JSON request body is required.");
        try { return JsonSerializer.Deserialize<T>(buffer.ToArray(), RequestJson) ?? throw new JsonException(); }
        catch (JsonException) { throw InvalidValue("The settings request body is invalid."); }
    }

    public async Task<ControlSettingPage> ListAsync(CancellationToken cancellationToken = default)
    {
        var catalog = ReadCatalog();
        var heads = await HeadsAsync(cancellationToken);
        return new(RuntimeName(catalog.Runtime.State), catalog.Runtime.Message,
            catalog.Definitions.Select(definition => Project(
                definition, catalog.Runtime.State, heads.GetValueOrDefault(definition.Key))).ToArray());
    }

    // Compatibility for web-only consumers with no persistence registration.
    public ControlSettingPage List() => ListAsync().GetAwaiter().GetResult();

    public async Task<ControlSettingDetail?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var catalog = ReadCatalog();
        var definition = catalog.Definitions.SingleOrDefault(candidate => candidate.Key == key);
        if (definition is null) return null;
        var heads = await HeadsAsync(cancellationToken);
        return new(Project(definition, catalog.Runtime.State, heads.GetValueOrDefault(key)), definition.Schema.Clone());
    }

    public ControlSettingDetail? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public async Task<ControlSettingVersionPage> ListVersionsAsync(
        string key, string? beforeVersion, string? limit, CancellationToken cancellationToken = default)
    {
        var definition = Definition(key);
        var before = ParseOptionalPositive(beforeVersion, "beforeVersion");
        var count = ParseLimit(limit);
        var persistence = RequireStore();
        var heads = await persistence.GetHeadsAsync(cancellationToken);
        if (!heads.TryGetValue(key, out var head)) return new([], null);
        var rows = await persistence.ListVersionsAsync(key, before, count + 1, cancellationToken);
        var more = rows.Count > count;
        var page = rows.Take(count).Select(row => VersionItem(definition, head, row)).ToArray();
        return new(page, more ? page[^1].Version : null);
    }

    public Task<ControlSettingWriteResult> UpdateAsync(
        string key, ControlSettingUpdateRequest request, string actor, CancellationToken cancellationToken = default)
    {
        if (request.ExpectedRevision is null || request.ExpectedRevision < 0 ||
            request.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw InvalidValue("An expectedRevision and a non-null setting value are required.");
        JsonElement normalized;
        try { normalized = provider.NormalizeOverride(Definition(key).Key, request.Value); }
        catch (KeyNotFoundException) { throw Unknown(key); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        { throw InvalidValue(exception.Message); }
        return AppendAsync(key, request.ExpectedRevision.Value, JsonSerializer.Serialize(normalized), actor,
            "control.settings.update", null, cancellationToken);
    }

    public Task<ControlSettingWriteResult> ResetAsync(
        string key, ControlSettingResetRequest request, string actor, CancellationToken cancellationToken = default)
    {
        Definition(key);
        if (request.ExpectedRevision is null || request.ExpectedRevision < 0)
            throw InvalidValue("A non-negative expectedRevision is required.");
        return AppendAsync(key, request.ExpectedRevision.Value, null, actor,
            "control.settings.reset", null, cancellationToken);
    }

    public Task<ControlSettingWriteResult> RollbackAsync(
        string key, ControlSettingRollbackRequest request, string actor, CancellationToken cancellationToken = default)
    {
        Definition(key);
        if (request.ExpectedRevision is null || request.ExpectedRevision < 1 ||
            request.TargetRevision is null || request.TargetRevision < 1 ||
            request.TargetRevision >= request.ExpectedRevision)
            throw InvalidValue("Rollback requires a current expectedRevision and an earlier positive targetRevision.");
        return AppendAsync(key, request.ExpectedRevision.Value, null, actor,
            "control.settings.rollback", request.TargetRevision, cancellationToken);
    }

    private async Task<ControlSettingWriteResult> AppendAsync(
        string key, int expected, string? valueJson, string actor, string tool, int? rollback,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RequireStore().AppendAsync(
                new(key, expected, valueJson, NormalizeActor(actor), tool, rollback), cancellationToken);
            return new(result.Revision.Version, result.AppliedVersion, result.Revision.OperationId, true);
        }
        catch (HostSettingOverrideStoreException exception)
        {
            var status = exception.Code == "SETTING_REVISION_UNKNOWN"
                ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict;
            throw new ControlSettingsException(exception.Code, exception.Message, status);
        }
    }

    private HostSettingDefinition Definition(string key)
    {
        ValidateKey(key);
        return ReadCatalog().Definitions.SingleOrDefault(definition => definition.Key == key) ?? throw Unknown(key);
    }

    private async Task<IReadOnlyDictionary<string, HostSettingOverrideHead>> HeadsAsync(CancellationToken cancellationToken) =>
        store is null ? new Dictionary<string, HostSettingOverrideHead>(StringComparer.Ordinal) :
        await store.GetHeadsAsync(cancellationToken);

    private IHostSettingOverrideStore RequireStore() => store ?? throw new ControlSettingsException(
        "HOST_SETTING_STORE_UNAVAILABLE", "Host setting override storage is unavailable.",
        StatusCodes.Status503ServiceUnavailable);

    private HostSettingCatalog ReadCatalog()
    {
        var catalog = provider.GetCatalog() ?? throw InvalidCatalog("The host setting provider returned no catalog.");
        if (catalog.Runtime is null || catalog.Definitions is null ||
            string.IsNullOrWhiteSpace(catalog.Runtime.Message) || catalog.Runtime.Message.Length > 500 ||
            catalog.Definitions.Count > MaximumDefinitions)
            throw InvalidCatalog("The host setting catalog is incomplete or exceeds its bound.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in catalog.Definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Key) || definition.Key.Length > 100 ||
                !SettingKeyPattern().IsMatch(definition.Key) || !seen.Add(definition.Key) ||
                string.IsNullOrWhiteSpace(definition.DisplayName) || definition.DisplayName.Length > 100 ||
                string.IsNullOrWhiteSpace(definition.Description) || definition.Description.Length > 500 ||
                definition.Schema.ValueKind is not JsonValueKind.Object)
                throw InvalidCatalog("The host setting catalog contains an invalid or duplicate definition.");
            if (definition.Source is not ("default" or "configuration" or "override") ||
                definition.Configured != (definition.Source is "configuration" or "override"))
                throw InvalidCatalog($"The host setting definition '{definition.Key}' has an invalid source.");
        }
        return catalog;
    }

    private static ControlSettingSummary Project(
        HostSettingDefinition definition, HostSettingRuntimeState runtimeState, HostSettingOverrideHead? head)
    {
        var redact = definition.Sensitivity is HostSettingSensitivity.ConfiguredOnly;
        var pending = head is not null && head.CurrentVersion > head.AppliedVersion;
        JsonElement? pendingValue = pending && head!.ValueJson is not null ? Parse(head.ValueJson) : null;
        return new(definition.Key, definition.DisplayName, definition.Description,
            SensitivityName(definition.Sensitivity), MutabilityName(definition.Mutability),
            DisruptionName(definition.Disruption), definition.Source, definition.Configured,
            RuntimeName(runtimeState), redact ? null : Clone(definition.Value),
            redact ? null : Clone(definition.EffectiveValue), redact ? null : pendingValue,
            pending, head?.CurrentVersion ?? 0, head?.AppliedVersion ?? 0);
    }

    private static ControlSettingVersionItem VersionItem(
        HostSettingDefinition definition, HostSettingOverrideHead head, HostSettingOverrideRevision row)
    {
        var state = row.Version == head.CurrentVersion && head.CurrentVersion > head.AppliedVersion
            ? "pending" : row.Version == head.AppliedVersion ? "applied" : "history";
        JsonElement? value = definition.Sensitivity == HostSettingSensitivity.ConfiguredOnly || row.ValueJson is null
            ? null : Parse(row.ValueJson);
        return new(row.Version, state, row.CreatedAtUtc, row.CreatedBy, row.OperationId, row.ValueJson is null, value);
    }

    private static JsonElement Parse(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static JsonElement? Clone(JsonElement? value) => value?.Clone();
    private static string NormalizeActor(string actor)
    {
        var value = string.IsNullOrWhiteSpace(actor) ? "local-operator" : actor.Trim();
        return value[..Math.Min(value.Length, 200)];
    }
    private static int ParseLimit(string? value) => string.IsNullOrWhiteSpace(value) ? 25 :
        int.TryParse(value, out var parsed) && parsed is >= 1 and <= 100 ? parsed :
        throw InvalidValue("limit must be an integer from 1 through 100.");
    private static int? ParseOptionalPositive(string? value, string name) => string.IsNullOrWhiteSpace(value) ? null :
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed :
        throw InvalidValue($"{name} must be a positive integer.");
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || !SettingKeyPattern().IsMatch(key))
            throw new ControlSettingsException("INVALID_SETTING_KEY", "The setting key must be a bounded lowercase dotted or dashed identifier.", StatusCodes.Status400BadRequest);
    }
    private static ControlSettingsException Unknown(string key) => new(
        "SETTING_UNKNOWN", $"Host setting '{key}' is not registered.", StatusCodes.Status404NotFound);
    private static ControlSettingsException InvalidValue(string message) => new(
        "INVALID_SETTING_VALUE", message, StatusCodes.Status400BadRequest);
    private static ControlSettingsException InvalidCatalog(string message) => new(
        "HOST_SETTING_CATALOG_INVALID", message, StatusCodes.Status500InternalServerError);
    private static string RuntimeName(HostSettingRuntimeState value) => value switch
    { HostSettingRuntimeState.Ready => "ready", HostSettingRuntimeState.NotRegistered => "not-registered", HostSettingRuntimeState.Unavailable => "unavailable", _ => throw InvalidCatalog("Invalid runtime state.") };
    private static string SensitivityName(HostSettingSensitivity value) => value switch
    { HostSettingSensitivity.PublicValue => "public-value", HostSettingSensitivity.ConfiguredOnly => "configured-only", _ => throw InvalidCatalog("Invalid sensitivity.") };
    private static string MutabilityName(HostSettingMutability value) => value switch
    { HostSettingMutability.ReadOnly => "read-only", HostSettingMutability.Live => "live", HostSettingMutability.RestartRequired => "restart-required", _ => throw InvalidCatalog("Invalid mutability.") };
    private static string DisruptionName(HostSettingDisruption value) => value switch
    { HostSettingDisruption.None => "none", HostSettingDisruption.LocalCompletion => "local-completion", HostSettingDisruption.HostRestart => "host-restart", _ => throw InvalidCatalog("Invalid disruption.") };

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SettingKeyPattern();
}
